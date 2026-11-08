using System;
using System.Reflection;

namespace FiscalOverhaul.Logic
{
    /// <summary>What the vanilla demand systems read their tax rates from, as last set (and logged).</summary>
    public enum DemandReads
    {
        /// <summary>Not set since the system started: neither line has been logged.</summary>
        Unknown = 0,
        /// <summary>Vanilla TaxSystem's rates.</summary>
        Vanilla = 1,
        /// <summary>The adjusted demand view.</summary>
        View = 2,
    }

    /// <summary>
    /// The demand view's swap, as pure logic: which field to swap on each demand system, and when the view is used.
    /// Each vanilla demand system declares its own private <c>m_TaxSystem</c>; a FieldInfo taken from one type throws
    /// ArgumentException when used on another, so every type gets its own.
    /// </summary>
    public static class DemandSwapLogic
    {
        public const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        /// <summary>
        /// The field <paramref name="name"/> of type <paramref name="expected"/> on each of <paramref name="types"/>, looked
        /// up on that type itself, in the same order. Null, with <paramref name="problem"/> naming the first type that lacks
        /// it (or has it with another type), when any one doesn't fit.
        /// </summary>
        public static FieldInfo[] ResolveFields(Type[] types, string name, Type expected, out string problem)
        {
            problem = null;
            if (types == null || types.Length == 0)
            {
                problem = "no demand system types";
                return null;
            }
            var fields = new FieldInfo[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                FieldInfo field = type?.GetField(name, InstanceFields);
                if (field == null)
                {
                    problem = $"{type?.Name ?? "null"}.{name} is missing";
                    return null;
                }
                if (expected != null && field.FieldType != expected)
                {
                    problem = $"{type.Name}.{name} is {field.FieldType.Name}, expected {expected.Name}";
                    return null;
                }
                fields[i] = field;
            }
            return fields;
        }

        /// <summary>
        /// Whether each field can be read on the instance at the same index (its declaring type is the instance's type or
        /// a base of it). <paramref name="problem"/> names the first one that can't.
        /// </summary>
        public static bool FitsInstances(FieldInfo[] fields, object[] instances, out string problem)
        {
            problem = null;
            if (fields == null || instances == null || fields.Length != instances.Length)
            {
                problem = $"{fields?.Length ?? 0} fields for {instances?.Length ?? 0} demand systems";
                return false;
            }
            for (int i = 0; i < fields.Length; i++)
            {
                if (instances[i] == null || !fields[i].DeclaringType.IsInstanceOfType(instances[i]))
                {
                    problem = $"{fields[i].DeclaringType.Name}.{fields[i].Name} isn't a field of {instances[i]?.GetType().Name ?? "null"}";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Whether demand should read the view: the structure fits, the engine charges, and the swap hasn't faulted (a
        /// fault falls back to vanilla demand for the rest of the session instead of throwing every update).
        /// </summary>
        public static bool UseView(bool structureOk, bool engineCharging, bool faulted)
            => structureOk && engineCharging && !faulted;

        /// <summary>Whether moving from <paramref name="current"/> to <paramref name="next"/> is a change worth a log line.</summary>
        public static bool ShouldLog(DemandReads current, DemandReads next) => current != next && next != DemandReads.Unknown;
    }
}
