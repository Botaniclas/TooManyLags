using System;
using System.Reflection;
using System.Reflection.Emit;

namespace TooManyLags.PlayerSimulation
{
    // Reflection is used only while wiring the plugin. Invocation emits a direct,
    // read-only ldfld, including native private fields.
    internal static class FieldReader
    {
        internal static Func<TSource, TValue> Create<TSource, TValue>(FieldInfo field)
        {
            if (field == null || field.IsStatic || !typeof(TValue).IsAssignableFrom(field.FieldType) ||
                typeof(TSource).IsValueType || field.DeclaringType.IsValueType)
                throw new NotSupportedException("Unsupported read-only field getter.");
            DynamicMethod getter = new DynamicMethod("TooManyLagsRead_" + field.Name, typeof(TValue),
                new[] { typeof(TSource) }, typeof(FieldReader).Module, true);
            ILGenerator il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            if (typeof(TSource) != field.DeclaringType) il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return (Func<TSource, TValue>)getter.CreateDelegate(typeof(Func<TSource, TValue>));
        }
    }
}
