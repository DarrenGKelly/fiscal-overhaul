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
    /// </summary>
    public partial class TaxBookingSystem : GameSystemBase
    {
        private TaxEngineSystem m_Engine;
        private CityServiceBudgetSystem m_BudgetSystem;
        private uint m_BookedVersion;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Engine = World.GetOrCreateSystemManaged<TaxEngineSystem>();
            m_BudgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            uint version = m_BudgetSystem.LastSystemVersion;
            if (version == m_BookedVersion) return; // vanilla didn't recompute: what's there is already booked
            m_BookedVersion = version;
            if (m_Engine.Mode != TaxEngineMode.Engine || m_Engine.Ledger.IsEmpty) return;

            var parts = new NativeArray<long>(TaxBookingLedger.Zones * 3, Allocator.TempJob);
            for (int z = 0; z < TaxBookingLedger.Zones; z++)
            {
                m_Engine.Ledger.Monthly((TaxZone)z, out long district, out long positive, out long negative);
                parts[z * 3] = district;
                parts[z * 3 + 1] = positive;
                parts[z * 3 + 2] = negative;
            }
            NativeArray<int> income = m_BudgetSystem.GetIncomeArray(out JobHandle incomeDeps);
            NativeArray<int> expenses = m_BudgetSystem.GetExpenseArray(out JobHandle expenseDeps);
            var job = new BookJob { m_Parts = parts, m_Income = income, m_Expenses = expenses };
            JobHandle handle = job.Schedule(JobHandle.CombineDependencies(Dependency, incomeDeps, expenseDeps));
            m_BudgetSystem.AddArrayReader(handle);
            parts.Dispose(handle);
            Dependency = handle;
        }

        [BurstCompile]
        private struct BookJob : IJob
        {
            [ReadOnly] public NativeArray<long> m_Parts;
            public NativeArray<int> m_Income;
            public NativeArray<int> m_Expenses;

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
                DensityTaxBooking.BookZone(m_Income[incomeSource], m_Expenses[subsidySource], m_Parts[z], m_Parts[z + 1], m_Parts[z + 2],
                    out int income, out int subsidy);
                m_Income[incomeSource] = income;
                m_Expenses[subsidySource] = subsidy;
            }
        }
    }
}
