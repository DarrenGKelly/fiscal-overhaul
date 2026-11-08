using FiscalOverhaul.Logic;
using Game.Agents;
using Game.Citizens;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace FiscalOverhaul
{
    /// <summary>Which tax a payer pays, as the engine's queries split them (vanilla PayTaxJob's IncomeSource).</summary>
    public enum TaxPayerKind
    {
        Household = 0,
        Commercial = 1,
        /// <summary>A processing company: Industrial, or Office when its output weighs nothing (<see cref="TaxPayerZone"/>).</summary>
        Industrial = 2,
    }

    /// <summary>
    /// THE effective-rate computation, shared by everything that needs the rate a payer is really charged (the tax
    /// engine, the demand view, happiness, home choice and the Taxation tab's estimate): the payer's vanilla rate
    /// (<c>TaxPayer.m_AverageTaxRate</c>, blended from the city's rates by the income systems) plus
    /// <list type="bullet">
    /// <item>its district's income-tax difference: for a household the wage-weighted mean, over its workers' education
    /// levels, of (district rate − city rate); for a company (district rate − city rate) for its output resource
    /// (<see cref="TaxRateModel"/>);</item>
    /// <item>its density tax: the district's rate for its building's (zone, density) cell, else the city's (0 while
    /// density taxes are off);</item>
    /// </list>
    /// clamped by <see cref="EffectiveTaxRate.Compute"/>. A payer outside every district (or with no property) pays the
    /// city's values. Burst-safe: build one on the main thread with <see cref="Create"/>, hand it to a job, then
    /// <see cref="Dispose(JobHandle)"/> with the job's handle.
    /// </summary>
    public struct TaxRateResolver
    {
        /// <summary>District entity → block index into <see cref="m_Deltas"/> and (block + 1) into <see cref="m_Density"/>.</summary>
        [ReadOnly] public NativeParallelHashMap<Entity, int> m_DistrictBlocks;
        /// <summary><see cref="TaxRateModel.BlockSize"/> ints per district with overrides (<see cref="TaxRateModel.DeltaIndex"/>).</summary>
        [ReadOnly] public NativeArray<int> m_Deltas;
        /// <summary><see cref="TaxRateModel.DensityCells"/> ints per area: block 0 the city's, block i + 1 district block i's.</summary>
        [ReadOnly] public NativeArray<int> m_Density;
        /// <summary>The weight of each education level in a household's mean: its wage (EconomyParameterData.GetWage).</summary>
        [ReadOnly] public NativeArray<int> m_WageByLevel;
        /// <summary>Vanilla TaxSystem's 92 rates as they were when the resolver was built (the deltas are against them).</summary>
        [ReadOnly] public NativeArray<int> m_CityRates;
        public TaxKeyResolver m_Keys;
        [ReadOnly] public BufferLookup<HouseholdCitizen> m_HouseholdCitizens;
        [ReadOnly] public ComponentLookup<Worker> m_Workers;
        [ReadOnly] public ComponentLookup<PrefabRef> m_Prefabs;
        [ReadOnly] public ComponentLookup<IndustrialProcessData> m_ProcessDatas;
        [ReadOnly] public ComponentLookup<ResourceData> m_ResourceDatas;
        [ReadOnly] public ResourcePrefabs m_ResourcePrefabs;

        /// <summary>
        /// Builds the resolver from the loaded city's table (<see cref="DensityTaxState"/>), vanilla's rates and limits
        /// (<paramref name="taxSystem"/>) and the wages: main thread only. The arrays are the caller's to dispose.
        /// </summary>
        public static TaxRateResolver Create(SystemBase system, TaxSystem taxSystem, ResourcePrefabs prefabs, NativeArray<int> wageByLevel, Allocator allocator)
        {
            TaxRateTable table = DensityTaxState.Table.Rates;
            bool densityOn = DensityTaxState.Enabled;
            int[] cityRates = CityRates(taxSystem);
            TaxLimits limits = LimitsFrom(taxSystem.GetTaxParameterData());

            // The districts with any override, in a stable order.
            var districts = new System.Collections.Generic.List<(int, int)>();
            foreach (TaxRateEntry e in table.Entries())
                if (!e.IsCity && !districts.Contains((e.Area, e.AreaVersion))) districts.Add((e.Area, e.AreaVersion));

            var blocks = new NativeParallelHashMap<Entity, int>(math.max(1, districts.Count), allocator);
            var deltas = new NativeArray<int>(math.max(1, districts.Count) * TaxRateModel.BlockSize, allocator);
            var density = new NativeArray<int>((districts.Count + 1) * TaxRateModel.DensityCells, allocator);
            int[] city = TaxRateModel.DistrictDensityRates(table, TaxRateTable.CityArea, 0, densityOn);
            NativeArray<int>.Copy(city, 0, density, 0, city.Length);
            for (int i = 0; i < districts.Count; i++)
            {
                (int index, int version) = districts[i];
                blocks[new Entity { Index = index, Version = version }] = i;
                int[] d = TaxRateModel.DistrictDeltas(table, index, version, cityRates, limits);
                NativeArray<int>.Copy(d, 0, deltas, i * TaxRateModel.BlockSize, d.Length);
                int[] cells = TaxRateModel.DistrictDensityRates(table, index, version, densityOn);
                NativeArray<int>.Copy(cells, 0, density, (i + 1) * TaxRateModel.DensityCells, cells.Length);
            }

            return new TaxRateResolver
            {
                m_DistrictBlocks = blocks,
                m_Deltas = deltas,
                m_Density = density,
                m_WageByLevel = wageByLevel,
                m_CityRates = new NativeArray<int>(cityRates, allocator),
                m_Keys = TaxKeyResolver.Create(system),
                m_HouseholdCitizens = system.GetBufferLookup<HouseholdCitizen>(true),
                m_Workers = system.GetComponentLookup<Worker>(true),
                m_Prefabs = system.GetComponentLookup<PrefabRef>(true),
                m_ProcessDatas = system.GetComponentLookup<IndustrialProcessData>(true),
                m_ResourceDatas = system.GetComponentLookup<ResourceData>(true),
                m_ResourcePrefabs = prefabs,
            };
        }

        /// <summary>Frees the resolver's own arrays (not the wages, which the caller passed in) after <paramref name="dependency"/>.</summary>
        public void Dispose(JobHandle dependency)
        {
            m_DistrictBlocks.Dispose(dependency);
            m_Deltas.Dispose(dependency);
            m_Density.Dispose(dependency);
            m_CityRates.Dispose(dependency);
        }

        /// <summary>
        /// The wage per education level (EconomyParameterData.GetWage), the household weights: main thread; all 0 when
        /// the query has no singleton yet. The caller disposes it (after the resolver's jobs).
        /// </summary>
        public static NativeArray<int> Wages(EntityQuery economyParameterQuery, Allocator allocator)
        {
            var wages = new NativeArray<int>(TaxRateModel.JobLevels, allocator);
            if (!economyParameterQuery.IsEmptyIgnoreFilter)
            {
                EconomyParameterData economy = economyParameterQuery.GetSingleton<EconomyParameterData>();
                for (int level = 0; level < TaxRateModel.JobLevels; level++) wages[level] = economy.GetWage(level);
            }
            return wages;
        }

        /// <summary>Vanilla TaxSystem's rate array as plain ints (main thread).</summary>
        public static int[] CityRates(TaxSystem taxSystem) => taxSystem.GetTaxRates().ToArray();

        /// <summary>Vanilla's limits in the model's form.</summary>
        public static TaxLimits LimitsFrom(TaxParameterData p)
        {
            var l = new TaxLimits
            {
                JobLevelMin = p.m_JobLevelTaxLimits.x,
                JobLevelMax = p.m_JobLevelTaxLimits.y,
                ResourceMin = p.m_ResourceTaxLimits.x,
                ResourceMax = p.m_ResourceTaxLimits.y,
            };
            int2[] zones = { p.m_ResidentialTaxLimits, p.m_CommercialTaxLimits, p.m_IndustrialTaxLimits, p.m_OfficeTaxLimits };
            for (int z = 0; z < zones.Length; z++)
            {
                l.ZoneMin[z] = zones[z].x;
                l.ZoneMax[z] = zones[z].y;
            }
            return l;
        }

        /// <summary>
        /// What the city's values and the payer's district add to its vanilla rate, before the clamp: its income-tax
        /// difference plus its density tax. <paramref name="property"/> is its <c>PropertyRenter.m_Property</c>
        /// (Entity.Null: none, so nothing is added).
        /// </summary>
        public void Adjustments(Entity payer, Entity property, TaxPayerKind kind, out int incomeDelta, out int densityTax)
        {
            incomeDelta = 0;
            bool isHousehold = kind == TaxPayerKind.Household;
            if (!Locate(property, isHousehold, out int block, out densityTax) || block < 0) return;
            int start = block * TaxRateModel.BlockSize;
            if (isHousehold)
            {
                incomeDelta = HouseholdDelta(payer, start);
                return;
            }
            if (!TryOutput(payer, out Resource resource, out bool weightless)) return;
            TaxZone zone = TaxPayerZone.Of(false, kind == TaxPayerKind.Commercial, weightless);
            int key = EconomyUtils.GetResourceIndex(resource);
            if (key < 0 || key >= TaxRateModel.ResourceCount) return;
            incomeDelta = m_Deltas[start + TaxRateModel.DeltaIndex(zone, key)];
        }

        /// <summary>
        /// The rate a payer is charged: <see cref="EffectiveTaxRate.Compute"/> of its vanilla rate and
        /// <see cref="Adjustments"/>, inside <paramref name="rateLimits"/> (vanilla's limits for its kind).
        /// </summary>
        public int ChargedRate(in TaxPayer taxPayer, Entity payer, Entity property, TaxPayerKind kind, int2 rateLimits)
        {
            Adjustments(payer, property, kind, out int incomeDelta, out int densityTax);
            return EffectiveTaxRate.Compute(taxPayer.m_AverageTaxRate, densityTax, incomeDelta, rateLimits.x, rateLimits.y, taxPayer.m_UntaxedIncome);
        }

        /// <summary>
        /// Finds <paramref name="property"/>'s district block (-1: none, or a district with no override) and the density
        /// tax its building pays. False when it adds nothing at all (no property, no district override and no density
        /// tax), so a caller can keep vanilla's own result.
        /// </summary>
        public bool Locate(Entity property, bool isHousehold, out int block, out int densityTax)
        {
            block = -1;
            densityTax = 0;
            if (property == Entity.Null) return false;
            if (!m_DistrictBlocks.IsEmpty)
            {
                Entity district = m_Keys.District(property);
                if (district != Entity.Null && m_DistrictBlocks.TryGetValue(district, out int b)) block = b;
            }
            if (m_Keys.TryDensityCell(property, isHousehold, out int cell))
                densityTax = m_Density[(block + 1) * TaxRateModel.DensityCells + cell];
            return block >= 0 || densityTax != 0;
        }

        /// <summary>
        /// The residential rate a resident at education <paramref name="level"/> of a home pays (happiness, home
        /// choice, demand): <paramref name="vanillaLevelRate"/> (the city's rate for the level) plus the home's district
        /// difference at that level only, plus its density tax, inside the job-level <paramref name="limits"/>
        /// (<see cref="TaxRateModel.ResidentRate"/>). <paramref name="block"/> and <paramref name="densityTax"/> are
        /// <see cref="Locate"/>'s.
        /// </summary>
        public int ResidentRate(int block, int densityTax, int level, int vanillaLevelRate, int2 limits)
        {
            int levelDelta = 0;
            if (block >= 0 && level >= 0 && level < TaxRateModel.JobLevels)
                levelDelta = m_Deltas[block * TaxRateModel.BlockSize + TaxRateModel.DeltaIndex(TaxZone.Residential, level)];
            return TaxRateModel.ResidentRate(vanillaLevelRate, levelDelta, densityTax, limits.x, limits.y);
        }

        /// <summary><see cref="ResidentRate(int, int, int, int, int2)"/> for a home entity (Entity.Null: the vanilla rate).</summary>
        public int ResidentRate(Entity property, int level, int vanillaLevelRate, int2 limits)
        {
            if (!Locate(property, true, out int block, out int densityTax)) return vanillaLevelRate;
            return ResidentRate(block, densityTax, level, vanillaLevelRate, limits);
        }

        /// <summary>The city's residential rate for an education level, as vanilla computes it from the resolver's rates.</summary>
        public int CityResidentRate(int level) => TaxSystem.GetResidentialTaxRate(level, m_CityRates);

        /// <summary>Whether an industrial payer's output weighs nothing, so vanilla books its tax as Office.</summary>
        public bool IsWeightless(Entity payer) => TryOutput(payer, out _, out bool weightless) && weightless;

        private bool TryOutput(Entity payer, out Resource resource, out bool weightless)
        {
            resource = Resource.NoResource;
            weightless = false;
            if (!m_Prefabs.HasComponent(payer)) return false;
            Entity prefab = m_Prefabs[payer].m_Prefab;
            if (!m_ProcessDatas.HasComponent(prefab)) return false;
            resource = m_ProcessDatas[prefab].m_Output.m_Resource;
            Entity resourcePrefab = m_ResourcePrefabs[resource];
            weightless = m_ResourceDatas.HasComponent(resourcePrefab) && m_ResourceDatas[resourcePrefab].m_Weight == 0f;
            return true;
        }

        /// <summary>The wage-weighted mean of the household's workers' level deltas (<see cref="TaxRateModel.HouseholdDelta"/>'s loop).</summary>
        private int HouseholdDelta(Entity household, int start)
        {
            long sum = 0, weight = 0;
            if (m_HouseholdCitizens.HasBuffer(household))
            {
                DynamicBuffer<HouseholdCitizen> citizens = m_HouseholdCitizens[household];
                for (int i = 0; i < citizens.Length; i++)
                {
                    Entity citizen = citizens[i].m_Citizen;
                    if (!m_Workers.HasComponent(citizen)) continue;
                    int level = math.clamp((int)m_Workers[citizen].m_Level, 0, TaxRateModel.JobLevels - 1);
                    int wage = m_WageByLevel.IsCreated && level < m_WageByLevel.Length ? m_WageByLevel[level] : 0;
                    long w = wage > 0 ? wage : 1;
                    sum += w * m_Deltas[start + TaxRateModel.DeltaIndex(TaxZone.Residential, level)];
                    weight += w;
                }
            }
            // No worker: other income is taxed at level 0's rate (PayWageSystem).
            if (weight == 0) return m_Deltas[start + TaxRateModel.DeltaIndex(TaxZone.Residential, 0)];
            return TaxRateModel.WeightedDelta(sum, weight);
        }
    }
}
