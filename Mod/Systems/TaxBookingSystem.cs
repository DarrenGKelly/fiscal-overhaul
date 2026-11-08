using System.Text;
using FiscalOverhaul.Logic;
using Game;
using Game.City;
using Game.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace FiscalOverhaul
{
    /// <summary>
    /// The city's side of the tax engine: the city receives what payers are charged. Vanilla pays the city its tax from
    /// CityServiceBudgetSystem's estimate (the city's rate per tax item × its taxable-income statistic; a negative item
    /// is the zone's subsidy expense), whatever the payer was charged. Right after CityServiceBudgetSystem computes this
    /// frame's arrays (which BudgetApplySystem pays out and the Budget tab shows), this adds the engine's charges beyond
    /// vanilla over the last month (<see cref="TaxEngineSystem.Ledger"/>), per zone, booked by
    /// <see cref="DensityTaxBooking.BookZone"/>: the district part on the zone's tax income, a positive density part on it
    /// too, a negative density part on the zone's subsidy expense.
    ///
    /// Vanilla rewrites both arrays from scratch every update, so this adds on top only once per vanilla update (checked
    /// by its <c>LastSystemVersion</c>), and writes nothing while the ledger is empty (every rate at the city's, density
    /// taxes off, simulation changes off, or the engine in its vanilla fallback): vanilla's numbers stand, bit for bit.
    ///
    /// While the engine charges it logs one line per 1/32 month (<see cref="TaxBookingLog"/>): per zone, vanilla's
    /// numbers, the engine's charges at vanilla rates (the scale check), the parts booked and the result.
    /// </summary>
    public partial class TaxBookingSystem : GameSystemBase
    {
        /// <summary>Frames between log lines: one statistics sample, 1/32 month.</summary>
        private const uint kLogInterval = 8192;
        private static readonly string[] kZoneNames = { "Residential", "Commercial", "Industrial", "Office" };
        private static readonly int[] kIncomeSources = { (int)IncomeSource.TaxResidential, (int)IncomeSource.TaxCommercial, (int)IncomeSource.TaxIndustrial, (int)IncomeSource.TaxOffice };
        private static readonly int[] kSubsidySources = { (int)ExpenseSource.SubsidyResidential, (int)ExpenseSource.SubsidyCommercial, (int)ExpenseSource.SubsidyIndustrial, (int)ExpenseSource.SubsidyOffice };

        private TaxEngineSystem m_Engine;
        private CityServiceBudgetSystem m_BudgetSystem;
        private SimulationSystem m_SimulationSystem;
        private uint m_BookedVersion;
        private uint m_LoggedSlot = uint.MaxValue;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Engine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
            m_BudgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            uint version = m_BudgetSystem.LastSystemVersion;
            if (version == m_BookedVersion) return; // vanilla didn't recompute: what's there is already booked
            m_BookedVersion = version;
            if (m_Engine.Mode != TaxEngineMode.Engine) return;

            uint slot = m_SimulationSystem.frameIndex / kLogInterval;
            bool log = slot != m_LoggedSlot;
            if (log) m_LoggedSlot = slot;

            var parts = new NativeArray<long>(TaxBookingLedger.Zones * 3, Allocator.TempJob);
            for (int z = 0; z < TaxBookingLedger.Zones; z++)
            {
                m_Engine.Ledger.Monthly((TaxZone)z, out long district, out long positive, out long negative);
                parts[z * 3] = district;
                parts[z * 3 + 1] = positive;
                parts[z * 3 + 2] = negative;
            }
            bool book = !m_Engine.Ledger.IsEmpty;
            NativeArray<int> income = m_BudgetSystem.GetIncomeArray(out JobHandle incomeDeps);
            NativeArray<int> expenses = m_BudgetSystem.GetExpenseArray(out JobHandle expenseDeps);
            if (!book)
            {
                // Vanilla's numbers stand; only the log reads them.
                if (log)
                {
                    JobHandle.CombineDependencies(incomeDeps, expenseDeps).Complete();
                    var vanilla = new int[TaxBookingLedger.Zones * 4];
                    for (int z = 0; z < TaxBookingLedger.Zones; z++)
                    {
                        vanilla[z * 4] = vanilla[z * 4 + 2] = income[kIncomeSources[z]];
                        vanilla[z * 4 + 1] = vanilla[z * 4 + 3] = expenses[kSubsidySources[z]];
                    }
                    Log(vanilla, parts);
                }
                parts.Dispose();
                return;
            }

            var report = new NativeArray<int>(TaxBookingLedger.Zones * 4, Allocator.TempJob);
            var job = new BookJob { m_Parts = parts, m_Income = income, m_Expenses = expenses, m_Report = report };
            JobHandle handle = job.Schedule(JobHandle.CombineDependencies(Dependency, incomeDeps, expenseDeps));
            m_BudgetSystem.AddArrayReader(handle);
            if (log)
            {
                // Once per 1/32 month: a tiny job, waited on for the line.
                handle.Complete();
                Log(report.ToArray(), parts);
            }
            parts.Dispose(handle);
            report.Dispose(handle);
            Dependency = handle;
        }

        /// <summary><paramref name="report"/>: per zone, vanilla income, vanilla subsidy, booked income, booked subsidy.</summary>
        private void Log(int[] report, NativeArray<long> parts)
        {
            var line = new StringBuilder("TaxBooking (monthly):");
            for (int z = 0; z < TaxBookingLedger.Zones; z++)
            {
                line.Append(z == 0 ? " " : " | ");
                line.Append(TaxBookingLog.Zone(kZoneNames[z], report[z * 4], report[z * 4 + 1], m_Engine.Ledger.VanillaMonthly((TaxZone)z),
                    parts[z * 3], parts[z * 3 + 1], parts[z * 3 + 2], report[z * 4 + 2], report[z * 4 + 3]));
            }
            Mod.log.Info(line.ToString());
        }

        [BurstCompile]
        private struct BookJob : IJob
        {
            [ReadOnly] public NativeArray<long> m_Parts;
            public NativeArray<int> m_Income;
            public NativeArray<int> m_Expenses;
            /// <summary>Per zone: vanilla income, vanilla subsidy, booked income, booked subsidy.</summary>
            public NativeArray<int> m_Report;

            public void Execute()
            {
                Book(TaxZone.Residential, (int)IncomeSource.TaxResidential, (int)ExpenseSource.SubsidyResidential);
                Book(TaxZone.Commercial, (int)IncomeSource.TaxCommercial, (int)ExpenseSource.SubsidyCommercial);
                Book(TaxZone.Industrial, (int)IncomeSource.TaxIndustrial, (int)ExpenseSource.SubsidyIndustrial);
                Book(TaxZone.Office, (int)IncomeSource.TaxOffice, (int)ExpenseSource.SubsidyOffice);
            }

            private void Book(TaxZone zone, int incomeSource, int subsidySource)
            {
                int z = (int)zone * 3;
                int vanillaIncome = m_Income[incomeSource], vanillaSubsidy = m_Expenses[subsidySource];
                DensityTaxBooking.BookZone(vanillaIncome, vanillaSubsidy, m_Parts[z], m_Parts[z + 1], m_Parts[z + 2],
                    out int income, out int subsidy);
                m_Income[incomeSource] = income;
                m_Expenses[subsidySource] = subsidy;
                int r = (int)zone * 4;
                m_Report[r] = vanillaIncome;
                m_Report[r + 1] = vanillaSubsidy;
                m_Report[r + 2] = income;
                m_Report[r + 3] = subsidy;
            }
        }
    }
}
