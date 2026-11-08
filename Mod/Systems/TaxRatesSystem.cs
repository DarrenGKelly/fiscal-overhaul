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
                // District keys are entities: written as such so the game remaps them on load.
                if (e.Type == TaxType.District)
                {
                    writer.Write(new Entity { Index = e.Key, Version = e.KeyVersion });
                }
                else
                {
                    writer.Write(e.Key);
                    writer.Write(e.KeyVersion);
                }
                writer.Write(e.Value);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            m_Rates.Clear();
            reader.Read(out int version);
            if (version == TaxRateTable.FormatVersion) // a format this build doesn't know: start empty
            {
                reader.Read(out int count);
                for (int i = 0; i < count; i++)
                {
                    reader.Read(out int type);
                    reader.Read(out int zone);
                    int key, keyVersion;
                    if ((TaxType)type == TaxType.District)
                    {
                        reader.Read(out Entity district);
                        key = district.Index;
                        keyVersion = district.Version;
                        reader.Read(out int districtValue);
                        // A district deleted before the save comes back as Entity.Null: drop its entry.
                        if (district != Entity.Null) m_Rates.Set(TaxType.District, zone, key, keyVersion, districtValue);
                        continue;
                    }
                    reader.Read(out key);
                    reader.Read(out keyVersion);
                    reader.Read(out int value);
                    m_Rates.Set((TaxType)type, zone, key, keyVersion, value);
                }
            }
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
