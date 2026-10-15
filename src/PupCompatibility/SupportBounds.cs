using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace TooManyLags.PupCompatibility
{
    internal abstract class SupportBounds
    {
        internal abstract bool Ready(WearNTear wear, NativePup pup);
        internal static SupportBounds Create()
        {
            FieldInfo field = typeof(WearNTear).GetField("m_bounds", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || !field.FieldType.IsGenericType || field.FieldType.GetGenericTypeDefinition() != typeof(List<>))
                throw new NotSupportedException("Native support bounds contract changed.");
            return (SupportBounds)Activator.CreateInstance(typeof(TypedBounds<>).MakeGenericType(field.FieldType.GetGenericArguments()[0]), new object[] { field });
        }
    }
    internal sealed class TypedBounds<T> : SupportBounds where T : struct
    {
        private readonly Func<WearNTear, List<T>> read;
        private readonly Func<T, Vector3> position, size;
        private readonly Func<T, Quaternion> rotation;
        private readonly Func<WearNTear, Collider[]> colliders;
        private readonly Action<WearNTear> setup;
        public TypedBounds(FieldInfo field)
        {
            read = FieldReader.Create<WearNTear, List<T>>(field);
            position = Getter<Vector3>("m_pos"); size = Getter<Vector3>("m_size"); rotation = Getter<Quaternion>("m_rot");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            colliders = FieldReader.Create<WearNTear, Collider[]>(typeof(WearNTear).GetField("m_colliders", flags));
            setup = (Action<WearNTear>)Delegate.CreateDelegate(typeof(Action<WearNTear>), typeof(WearNTear).GetMethod("SetupColliders", flags));
        }
        private static Func<T, V> Getter<V>(string name)
        {
            FieldInfo field = typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(V)) throw new MissingFieldException(typeof(T).FullName, name);
            DynamicMethod method = new DynamicMethod("BotanicSupport_" + name, typeof(V), new[] { typeof(T) }, typeof(SupportBounds).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarga_S, (byte)0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
            return (Func<T, V>)method.CreateDelegate(typeof(Func<T, V>));
        }
        internal override bool Ready(WearNTear wear, NativePup pup)
        {
            List<T> bounds = read(wear);
            if (bounds == null || colliders(wear) == null) { setup(wear); bounds = read(wear); }
            if (bounds == null) return false;
            if (bounds.Count == 0) return pup.ReadyPoint(wear.transform.position, 0.3f);
            foreach (T bound in bounds)
            {
                Vector3 half = size(bound); Quaternion q = rotation(bound);
                Vector3 x = q * new Vector3(half.x, 0f, 0f), y = q * new Vector3(0f, half.y, 0f), z = q * new Vector3(0f, 0f, half.z);
                Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
                if (!pup.ReadyBounds(new Bounds(position(bound), extents * 2f + Vector3.one * 0.1f))) return false;
            }
            return true;
        }
    }
}
