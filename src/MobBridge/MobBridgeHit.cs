#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags
{
    // Query-only copies of actual mob shapes, including bone-attached weak spots.
    // No copied scripts, gameplay bodies or network authority.
    internal sealed class MobBridgeHit
    {
        private sealed class Shape
        {
            internal Character Owner;
            internal Collider Native, Proxy;
            internal GameObject Root;
            internal Rigidbody Body;
            internal bool Enabled;
            internal bool Positioned;
            internal Vector3 Position, Scale;
            internal Quaternion Rotation;
        }
        private static readonly Dictionary<Collider, Shape> owners = new Dictionary<Collider, Shape>();
        private readonly List<Shape> shapes = new List<Shape>();
        private readonly Character actor;
        private readonly CapsuleCollider native;
        private readonly Transform visual;
        private bool disposed, restored;

        internal bool Alive { get { return !disposed && shapes.Count != 0 && actor; } }
        internal int Count { get { return shapes.Count; } }

        private MobBridgeHit(Character actor)
        { this.actor = actor; native = actor.GetCollider(); visual = actor.GetVisual().transform; }

        internal static MobBridgeHit Create(Character actor, Vector3 position, Quaternion rotation)
        {
            if (!actor || !actor.GetCollider() || !actor.GetVisual()) return null;
            int layer = LayerMask.NameToLayer("hitbox");
            if (layer < 0) return null;
            MobBridgeHit hit = new MobBridgeHit(actor);
            try
            {
                foreach (Collider source in actor.GetComponentsInChildren<Collider>(true))
                {
                    // Mounted/nested network entities keep their own hitboxes.
                    if (source.GetComponentInParent<Character>() != actor) continue;
                    ZNetView view = source.GetComponentInParent<ZNetView>();
                    if (view && view != actor.GetComponent<ZNetView>()) continue;
                    GameObject root = new GameObject("TooManyLags_BridgeHitbox");
                    Shape shape = new Shape { Owner = actor, Native = source, Root = root, Enabled = source.enabled };
                    hit.shapes.Add(shape); // Cleanup owns partially constructed shapes too.
                    root.SetActive(false); root.layer = layer;
                    root.transform.localScale = source.transform.lossyScale;
                    Collider proxy = Copy(source, root);
                    shape.Proxy = proxy;
                    proxy.sharedMaterial = source.sharedMaterial;
                    proxy.contactOffset = source.contactOffset; proxy.isTrigger = source.isTrigger;
                    proxy.enabled = shape.Enabled;
                    Rigidbody body = root.AddComponent<Rigidbody>();
                    body.isKinematic = true; body.useGravity = false; shape.Body = body;
                    owners.Add(proxy, shape);
                }
                if (hit.shapes.Count == 0) { hit.Dispose(); return null; }
                // Disable native shapes only after every copy succeeds.
                foreach (Shape shape in hit.shapes) shape.Native.enabled = false;
                hit.Follow(position, rotation);
                return hit;
            }
            catch { hit.Dispose(); throw; }
        }

        private static Collider Copy(Collider source, GameObject root)
        {
            CapsuleCollider capsule = source as CapsuleCollider;
            if (capsule)
            {
                CapsuleCollider copy = root.AddComponent<CapsuleCollider>();
                copy.center = capsule.center; copy.radius = capsule.radius;
                copy.height = capsule.height; copy.direction = capsule.direction; return copy;
            }
            SphereCollider sphere = source as SphereCollider;
            if (sphere)
            { SphereCollider copy = root.AddComponent<SphereCollider>(); copy.center = sphere.center; copy.radius = sphere.radius; return copy; }
            BoxCollider box = source as BoxCollider;
            if (box)
            { BoxCollider copy = root.AddComponent<BoxCollider>(); copy.center = box.center; copy.size = box.size; return copy; }
            MeshCollider mesh = source as MeshCollider;
            if (mesh)
            {
                MeshCollider copy = root.AddComponent<MeshCollider>();
                copy.cookingOptions = mesh.cookingOptions; copy.convex = mesh.convex;
                copy.sharedMesh = mesh.sharedMesh; return copy;
            }
            throw new NotSupportedException("Unsupported mob hitbox shape: " + source.GetType().Name);
        }

        internal static bool TryOwner(Collider collider, out Character owner)
        {
            Shape shape;
            if (collider != null && owners.TryGetValue(collider, out shape))
            { owner = shape.Owner; return true; }
            owner = null; return false;
        }

        internal static Collider NativeCollider(Character actor, Collider collider)
        {
            Shape shape;
            return collider != null && owners.TryGetValue(collider, out shape) && ReferenceEquals(shape.Owner, actor) ? shape.Native : collider;
        }

        internal void Follow(Vector3 position, Quaternion rotation, MobBridgeAnimation animation = null)
        {
            if (!Alive || restored) return;
            Quaternion inverse = Quaternion.Inverse(actor.transform.rotation);
            foreach (Shape shape in shapes)
            {
                if (!shape.Native || !shape.Proxy) { if (shape.Root) shape.Root.SetActive(false); continue; }
                Transform source = shape.Native.transform;
                // Bone query poses come from the immutable prepared sample;
                // root/attack capsules retain their actor-relative pose.
                bool bone = source.IsChildOf(visual);
                Vector3 p, scale; Quaternion q;
                if (!bone || animation == null || !animation.TryPose(source, out p, out q, out scale))
                {
                    p = bone ? source.position : position + rotation * (inverse * (source.position - actor.transform.position));
                    q = bone ? source.rotation : rotation * inverse * source.rotation; scale = source.lossyScale;
                }
                // Exact equality avoids redundant transform/broadphase work
                // without dropping tiny movement or changing the query shape.
                if (!shape.Positioned || !scale.Equals(shape.Scale)) shape.Root.transform.localScale = scale;
                if (!shape.Positioned || !p.Equals(shape.Position)) shape.Body.position = p;
                if (!shape.Positioned || !q.Equals(shape.Rotation)) shape.Body.rotation = q;
                shape.Position = p; shape.Rotation = q; shape.Scale = scale; shape.Positioned = true;
                bool active = shape.Native.gameObject.activeInHierarchy;
                if (shape.Root.activeSelf != active) shape.Root.SetActive(active);
            }
        }

        internal void RestorePhysics()
        {
            if (restored) return;
            restored = true;
            foreach (Shape shape in shapes)
            {
                if (shape.Root) shape.Root.SetActive(false);
                if (shape.Native) shape.Native.enabled = shape.Enabled;
            }
        }

        internal bool Accepts(HitData hit)
        {
            if (restored || hit == null || !hit.m_hitCollider) return true;
            foreach (Shape shape in shapes)
            {
                if (ReferenceEquals(hit.m_hitCollider, shape.Proxy)) return true;
                if (ReferenceEquals(hit.m_hitCollider, shape.Native)) return false;
            }
            return true;
        }

        internal bool HitsFakePose(HitData hit)
        {
            Character owner;
            return !restored && hit != null && TryOwner(hit.m_hitCollider, out owner) && ReferenceEquals(owner, actor);
        }

        internal Vector3 TopPoint(Vector3 position, Quaternion rotation, Quaternion visualLocalRotation)
        {
            return MobBridgePresentationPoint.TopPoint(position, rotation, visualLocalRotation,
                native.center, native.height, actor.transform.lossyScale);
        }

        internal void Dispose()
        {
            if (disposed) return;
            disposed = true;
            RestorePhysics();
            foreach (Shape shape in shapes)
            {
                if (!ReferenceEquals(shape.Proxy, null)) owners.Remove(shape.Proxy);
                if (shape.Root) UnityEngine.Object.Destroy(shape.Root);
            }
            shapes.Clear();
        }
    }
}
#endif
