using System;
using System.Reflection;
using FiscalOverhaul.Logic;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Mathematics;

namespace FiscalOverhaul
{
    /// <summary>
    /// The game-side half of the tax engine's guard (<see cref="TaxEngineGuardLogic"/> decides): reads the game version
    /// and checks, by reflection, that vanilla TaxSystem still has the shape <see cref="TaxEngineSystem"/>'s copy relies
    /// on. Checked once, when the engine system is created.
    /// </summary>
    public static class TaxEngineGuard
    {
        /// <summary>The length of TaxSystem.m_TaxRates the copy was taken against.</summary>
        public const int kTaxRateCount = 92;

        /// <summary>The running game's version (Game.Version.current.shortVersion), or "" when it can't be read.</summary>
        public static string GameVersion()
        {
            try
            {
                return Game.Version.current.shortVersion ?? string.Empty;
            }
            catch (Exception e)
            {
                Mod.log.Warn($"TaxEngine: can't read the game version: {e.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Whether vanilla TaxSystem has the private fields and rate array the copy assumes. <paramref name="problem"/>
        /// names the first mismatch.
        /// </summary>
        public static bool StructureOk(TaxSystem taxSystem, out string problem)
        {
            problem = null;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                Type type = typeof(TaxSystem);
                if (!HasField(type, "m_TaxRates", typeof(NativeArray<int>), flags, ref problem)) return false;
                if (!HasField(type, "m_TaxPaidMultiplier", typeof(float3), flags, ref problem)) return false;
                if (!HasField(type, "m_TaxParameterData", typeof(TaxParameterData), flags, ref problem)) return false;
                if (type.GetMethod("GetTax", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(Game.Agents.TaxPayer) }, null) == null)
                {
                    problem = "TaxSystem.GetTax(TaxPayer) is missing";
                    return false;
                }
                int length = taxSystem.GetTaxRates().Length;
                if (length != kTaxRateCount)
                {
                    problem = $"TaxSystem.GetTaxRates().Length is {length}, expected {kTaxRateCount}";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                problem = $"structure check threw: {e.Message}";
                return false;
            }
        }

        /// <summary>
        /// The vanilla demand systems whose private <c>m_TaxSystem</c> field <see cref="DemandTaxViewSystem"/> points at
        /// its view. Each reads only GetTaxRates() and AddReader() through it (game version 1.6.0f1:
        /// ResidentialDemandSystem.cs:487/502, CommercialDemandSystem.cs:610/630, IndustrialDemandSystem.cs:1173/1204).
        /// </summary>
        public static readonly Type[] DemandSystemTypes =
        {
            typeof(ResidentialDemandSystem), typeof(CommercialDemandSystem), typeof(IndustrialDemandSystem),
        };

        /// <summary>
        /// Whether the demand view's swap still fits the game: each <see cref="DemandSystemTypes"/> has a private
        /// <c>m_TaxSystem</c> field of type TaxSystem, and TaxSystem keeps its rates in <c>m_TaxRates</c>
        /// (<c>NativeArray&lt;int&gt;</c>), the field the view sets. <paramref name="problem"/> names the first mismatch.
        /// </summary>
        public static bool DemandStructureOk(out string problem)
        {
            problem = null;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                if (!HasField(typeof(TaxSystem), "m_TaxRates", typeof(NativeArray<int>), flags, ref problem)) return false;
                foreach (Type demand in DemandSystemTypes)
                {
                    FieldInfo field = demand.GetField("m_TaxSystem", flags);
                    if (field == null || field.FieldType != typeof(TaxSystem))
                    {
                        problem = $"{demand.Name}.m_TaxSystem is {(field == null ? "missing" : field.FieldType.Name + ", expected TaxSystem")}";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                problem = $"demand structure check threw: {e.Message}";
                return false;
            }
        }

        private static bool HasField(Type type, string name, Type expected, BindingFlags flags, ref string problem)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null)
            {
                problem = $"TaxSystem.{name} is missing";
                return false;
            }
            if (field.FieldType != expected)
            {
                problem = $"TaxSystem.{name} is {field.FieldType.Name}, expected {expected.Name}";
                return false;
            }
            return true;
        }
    }
}
