using Colossal.Serialization.Entities;
using FiscalOverhaul.Logic;
using Game;
using Unity.Entities;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// Keeps the loaded city's tax adjustments (<see cref="TaxRateTable"/>) in its save (<see cref="IDefaultSerializable"/>:
    /// the game serializes every world system implementing it, and a save loaded without the mod skips the block), and
    /// points <see cref="DensityTaxState"/> at them on every load.
    ///
    /// A city with no block (a new city, or a save from before rates were kept per save) gets <see cref="SetDefaults"/>:
    /// its density entries are seeded once from the Options template, <see cref="Setting.DensityTaxOffsets"/>, and from
    /// then on live in the save. Entries for districts that no longer exist are dropped on load.
    /// </summary>
    public partial class TaxRatesSystem : GameSystemBase, IDefaultSerializable, ISerializable
    {
        private Setting m_Setting;
        private readonly TaxRateTable m_Rates = new TaxRateTable();

        /// <summary>The loaded city's table. <see cref="DensityTaxState"/> writes it in place.</summary>
        public TaxRateTable Rates => m_Rates;

        public void SetSettings(Setting setting) => m_Setting = setting;

        [Preserve]
        public TaxRatesSystem() { }

        protected override void OnCreate()
        {
            base.OnCreate();
            DensityTaxState.Use(m_Rates);
        }

        /// <summary>Nothing per frame: the system exists for the save block and the load hooks.</summary>
        protected override void OnUpdate() { }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            int pruned = m_Rates.PruneDistricts(DistrictExists);
            if (pruned > 0) Mod.log.Info($"TaxRates: dropped {pruned} entries for districts that no longer exist");
            DensityTaxState.Use(m_Rates);
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var entries = m_Rates.Entries();
            writer.Write(TaxRateTable.FormatVersion);
            writer.Write(entries.Count);
            foreach (TaxRateEntry e in entries)
            {
                writer.Write((int)e.Type);
                writer.Write(e.Zone);
                writer.Write(e.Key);
                // The area is an entity (Entity.Null = the city), written as such so the game remaps it on load:
                // district entity indexes change from load to load.
                writer.Write(new Entity { Index = e.Area, Version = e.AreaVersion });
                writer.Write(e.Value);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            m_Rates.Clear();
            reader.Read(out int version);
            if (version == TaxRateTable.FormatVersion)
            {
                reader.Read(out int count);
                for (int i = 0; i < count; i++)
                {
                    reader.Read(out int type);
                    reader.Read(out int zone);
                    reader.Read(out int key);
                    reader.Read(out Entity area);
                    reader.Read(out int value);
                    // A district deleted before the save comes back as Entity.Null: its overrides are dropped, never
                    // read as the city's (only density has a city value in the table).
                    bool city = area == Entity.Null;
                    if (city && !TaxRateTable.HasCityValue((TaxType)type)) continue;
                    m_Rates.Set((TaxType)type, zone, key, area.Index, area.Version, value);
                }
            }
            else if (version == 1)
            {
                // The ±adjustment model (never shipped): density entries become the city's density tax rates; district
                // adjustments mean nothing as overrides and are dropped.
                reader.Read(out int count);
                for (int i = 0; i < count; i++)
                {
                    reader.Read(out int type);
                    reader.Read(out int zone);
                    int key = 0;
                    if ((TaxType)type == TaxType.District)
                    {
                        reader.Read(out Entity _);
                    }
                    else
                    {
                        reader.Read(out key);
                        reader.Read(out int _);
                    }
                    reader.Read(out int value);
                    TaxRateTable.LoadVersion1(m_Rates, (TaxType)type, zone, key, value);
                }
            }
            // Any other version: a format this build doesn't know: start empty.
            DensityTaxState.Use(m_Rates);
        }
        /// <summary>No block in the save: a new city or an older save. Seeds the density entries from the template once.</summary>
        public void SetDefaults(Context context)
        {
            m_Rates.Clear();
            string template = m_Setting?.DensityTaxOffsets;
            int seeded = m_Rates.SeedDensityFrom(template);
            if (seeded > 0) Mod.log.Info($"TaxRates: seeded {seeded} density offsets from the Options template");
            DensityTaxState.Use(m_Rates);
        }

        private bool DistrictExists(int index, int version)
        {
            var entity = new Entity { Index = index, Version = version };
            return EntityManager.Exists(entity) && EntityManager.HasComponent<Game.Areas.District>(entity);
        }
    }
}
