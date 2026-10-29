using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BotanicOwnerDebug
{
    // Read-only scope. Clients do not need Authority or its server-side config.
    internal static class BuildingVisuals
    {
        internal static bool Eligible(ZNetView view)
        {
            if (!view || !view.m_persistent || view.GetComponentInParent<Player>()) return false;
            ItemDrop item = view.GetComponentInParent<ItemDrop>();
            if (item && item.GetComponent<ZNetView>() != view) return false;
            if (view.GetComponent<Character>()) return !view.m_distant;
            if (view.GetComponentInParent<Character>()) return false;
            // Own network root only: stationary damageable world objects and
            // dynamic bodies (e.g. fallen logs). Native distant trees are valid
            // here; unrelated distant proxies remain excluded. Never call any
            // destructible/physics method or inspect velocities every frame.
            bool world = view.GetComponent<IDestructible>() != null || view.GetComponent<Rigidbody>();
            return world || (!view.m_distant && (view.GetComponent<Piece>() || view.GetComponent<WearNTear>() || Interactive(view)));
        }
        internal static bool Interactive(ZNetView view)
        {
            if (view.GetComponent<ItemDrop>()) return true;
            // Same interaction ownership criterion as Authority, without its
            // server-side categories/config or a dependency on that plugin.
            foreach (MonoBehaviour component in view.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!component || !(component is Interactable)) continue;
                Container container = component as Container;
                ZNetView owner = container && container.m_rootObjectOverride ? container.m_rootObjectOverride : component.GetComponentInParent<ZNetView>(true);
                if (owner == view) return true;
            }
            return false;
        }
        internal static bool Solid(Material source)
        {
            if (!source || !source.shader || source.renderQueue >= (int)RenderQueue.AlphaTest) return false;
            // Leave effect/alpha surfaces entirely on their original material,
            // even when they belong to an otherwise eligible building or mob.
            string tag = source.GetTag("RenderType", false, "");
            if (!String.IsNullOrEmpty(tag) && !String.Equals(tag, "Opaque", StringComparison.OrdinalIgnoreCase)) return false;
            if (source.HasProperty("_Mode") && source.GetFloat("_Mode") != 0f) return false;
            if (source.HasProperty("_SrcBlend") && source.GetInt("_SrcBlend") != (int)BlendMode.One) return false;
            if (source.HasProperty("_DstBlend") && source.GetInt("_DstBlend") != (int)BlendMode.Zero) return false;
            string shader = source.shader.name;
            return !EffectShader(shader);
        }
        private static bool EffectShader(string name)
        {
            if (String.IsNullOrEmpty(name)) return true;
            return name.StartsWith("Hidden/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("UI/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Skybox/", StringComparison.OrdinalIgnoreCase) ||
                Contains(name, "particle") || Contains(name, "effect") || Contains(name, "vfx") ||
                Contains(name, "billboard") || Contains(name, "distort") || Contains(name, "glow") ||
                Contains(name, "halo") || Contains(name, "flare") || Contains(name, "TextMeshPro") ||
                Contains(name, "smoke") || Contains(name, "fog") || Contains(name, "cloud") ||
                Contains(name, "beam") || Contains(name, "flame") || Contains(name, "depthonly") || Contains(name, "invisible");
        }
        private static bool Contains(string value, string token)
        { return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0; }
        internal static bool Tintable(Material source)
        {
            if (!Solid(source)) return false;
            if (source.HasProperty("_ZWrite") && source.GetInt("_ZWrite") == 0) return false;
            if (!source.HasProperty("_Color")) return true;
            Color native = source.GetColor("_Color");
            return native.a > 0f && !Single.IsInfinity(native.a) && !Single.IsNaN(native.r) && !Single.IsNaN(native.g) && !Single.IsNaN(native.b) && !Single.IsInfinity(native.r) && !Single.IsInfinity(native.g) && !Single.IsInfinity(native.b);
        }
    }
}
