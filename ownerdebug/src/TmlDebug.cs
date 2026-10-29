using System;
using System.Reflection;

namespace BotanicOwnerDebug
{
    internal static class TmlDebug
    {
        private static Func<ZNetView, int> category;
        private static Func<ZDO, int> phase;
        private static Func<ZDOID, bool> presenting;
        internal static bool Installed { get; private set; }
        internal static bool Supported { get { return phase != null; } }
        internal static void Bind(Assembly assembly)
        {
            Installed = assembly != null; category = null; phase = null; presenting = null;
            if (!Installed) return;
            // Existing read-only lookup; no TML source/build change required.
            Type visual = assembly.GetType("TooManyLags.MobBridgeVisual");
            MethodInfo contains = visual == null ? null : visual.GetMethod("Contains", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(ZDOID) }, null);
            if (contains != null && contains.ReturnType == typeof(bool))
                presenting = (Func<ZDOID, bool>)Delegate.CreateDelegate(typeof(Func<ZDOID, bool>), contains);
            Type api = assembly.GetType("TooManyLags.OwnerDebugApi");
            if (api == null) return;
            category = Read<ZNetView>(api, "Category"); phase = Read<ZDO>(api, "Phase");
        }
        private static Func<T, int> Read<T>(Type api, string name)
        {
            MethodInfo method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(T) }, null);
            return method != null && method.ReturnType == typeof(int) ?
                (Func<T, int>)Delegate.CreateDelegate(typeof(Func<T, int>), method) : null;
        }
        internal static int Category(ZNetView view) { return category == null ? DebugCategories.Classify(view) : category(view); }
        internal static int Phase(ZDO data) { return phase == null ? -1 : phase(data); }
        internal static bool Presenting(ZDO data)
        { return data != null && (presenting != null ? presenting(data.m_uid) : Phase(data) >= 2); }
    }
}
