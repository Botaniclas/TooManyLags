#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TooManyLags
{
    // The native Rigidbody runs in its own scene. Main-world collider shapes
    // are read-only inputs; no main-world body receives a prediction impulse.
    internal sealed class MobBridgeNativeWorld : IDisposable
    {
        private sealed class Shape
        {
            internal Collider Source, Copy;
            internal Transform SourceTransform;
            internal Rigidbody Body;
            internal Vector3 Position, TargetOffset;
            internal Quaternion Rotation, TargetTurn;
            internal Vector3 AppliedPosition;
            internal Quaternion AppliedRotation;
            internal bool Target, Enabled;
            internal PhysicsMaterial Material;
        }
        private readonly List<Shape> shapes = new List<Shape>();
        private readonly Dictionary<Collider, Shape> copied = new Dictionary<Collider, Shape>();
        private readonly Scene scene;
        private readonly PhysicsScene physics;
        private readonly GameObject environment;
        private static int sequence;

        internal MobBridgeNativeWorld(Character source, Vector3 origin, float seconds, MobBridgeCost cost)
        {
            scene = SceneManager.CreateScene("TooManyLags_MobRecording_" + (++sequence),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            physics = scene.GetPhysicsScene();
            if (!physics.IsValid()) throw new NotSupportedException("Private native physics scene unavailable.");
            environment = new GameObject("RecordingEnvironment");
            SceneManager.MoveGameObjectToScene(environment, scene);
            cost.Mark("scene");
            try
            {
                float speed = Mathf.Max(source.m_runSpeed, source.m_flyFastSpeed, source.GetVelocity().magnitude);
                CapsuleCollider capsule = source.GetCollider();
                float reach = speed * seconds + capsule.height * Mathf.Abs(source.transform.lossyScale.y) + capsule.radius;
                int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain",
                    "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
                foreach (Collider collider in Physics.OverlapSphere(origin, reach, mask, QueryTriggerInteraction.Ignore))
                {
                    Character owner;
                    if (!collider || collider.isTrigger || collider.transform.IsChildOf(source.transform) ||
                        MobBridgeHit.TryOwner(collider, out owner) && ReferenceEquals(owner, source)) continue;
                    Copy(collider);
                }
                cost.Mark("geometry"); cost.Count("shapes", shapes.Count);
            }
            catch { Dispose(); throw; }
        }

        internal void AddRoot(GameObject root) { SceneManager.MoveGameObjectToScene(root, scene); }

        private void Copy(Collider source)
        {
            if (!source || copied.ContainsKey(source)) return;
            GameObject root = new GameObject("Recording_" + source.name);
            root.SetActive(false); root.layer = source.gameObject.layer;
            root.transform.SetParent(environment.transform, false);
            root.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            root.transform.localScale = source.transform.lossyScale;
            Collider copy;
            BoxCollider box = source as BoxCollider;
            SphereCollider sphere = source as SphereCollider;
            CapsuleCollider capsule = source as CapsuleCollider;
            MeshCollider mesh = source as MeshCollider;
            TerrainCollider terrain = source as TerrainCollider;
            if (box)
            { BoxCollider shape = root.AddComponent<BoxCollider>(); shape.center = box.center; shape.size = box.size; copy = shape; }
            else if (sphere)
            { SphereCollider shape = root.AddComponent<SphereCollider>(); shape.center = sphere.center; shape.radius = sphere.radius; copy = shape; }
            else if (capsule)
            { CapsuleCollider shape = root.AddComponent<CapsuleCollider>(); shape.center = capsule.center; shape.radius = capsule.radius; shape.height = capsule.height; shape.direction = capsule.direction; copy = shape; }
            else if (mesh)
            { MeshCollider shape = root.AddComponent<MeshCollider>(); shape.cookingOptions = mesh.cookingOptions; shape.convex = mesh.convex; shape.sharedMesh = mesh.sharedMesh; copy = shape; }
            else if (terrain)
            { TerrainCollider shape = root.AddComponent<TerrainCollider>(); shape.terrainData = terrain.terrainData; copy = shape; }
            else { UnityEngine.Object.Destroy(root); throw new NotSupportedException("Unsupported native recording collider: " + source.GetType().Name); }
            copy.sharedMaterial = source.sharedMaterial; copy.contactOffset = source.contactOffset;
            Rigidbody body = null;
            if (source.attachedRigidbody)
            { body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false; body.mass = source.attachedRigidbody.mass; }
            bool enabled = source.enabled && source.gameObject.activeInHierarchy;
            copy.enabled = enabled;
            Shape entry = new Shape { Source = source, Copy = copy, SourceTransform = source.transform, Body = body, Position = source.transform.position, Rotation = source.transform.rotation,
                AppliedPosition = source.transform.position, AppliedRotation = source.transform.rotation, Enabled = enabled, Material = source.sharedMaterial };
            shapes.Add(entry); copied.Add(source, entry);
            root.SetActive(true);
        }

        internal void Observe(Character target)
        {
            Collider targetCollider = target ? target.GetCollider() : null;
            if (targetCollider && targetCollider.enabled) Copy(targetCollider);
            Transform targetRoot = target ? target.transform : null;
            Vector3 targetPosition = targetRoot ? targetRoot.position : Vector3.zero;
            Quaternion targetInverse = targetRoot ? Quaternion.Inverse(targetRoot.rotation) : Quaternion.identity;
            foreach (Shape shape in shapes)
            {
                if (!shape.Source)
                { if (shape.Copy && shape.Enabled) shape.Copy.enabled = false; shape.Enabled = false; continue; }
                shape.Position = shape.SourceTransform.position; shape.Rotation = shape.SourceTransform.rotation;
                shape.Target = targetRoot && shape.SourceTransform.IsChildOf(targetRoot);
                if (shape.Target)
                {
                    shape.TargetOffset = targetInverse * (shape.Position - targetPosition);
                    shape.TargetTurn = targetInverse * shape.Rotation;
                }
                bool enabled = shape.Source.enabled && shape.Source.gameObject.activeInHierarchy;
                if (shape.Enabled != enabled) { shape.Copy.enabled = enabled; shape.Enabled = enabled; }
            }
        }

        internal void Refresh(Vector3 targetPosition, Quaternion targetRotation)
        {
            foreach (Shape shape in shapes)
            {
                if (!shape.Copy || !shape.Enabled) continue;
                Vector3 position = shape.Target ? targetPosition + targetRotation * shape.TargetOffset : shape.Position;
                Quaternion rotation = shape.Target ? targetRotation * shape.TargetTurn : shape.Rotation;
                // Native static geometry needs no repeated broadphase writes.
                // Exact component equality preserves arbitrarily small changes.
                if (position.Equals(shape.AppliedPosition) && rotation.Equals(shape.AppliedRotation)) continue;
                if (shape.Body) { shape.Body.MovePosition(position); shape.Body.MoveRotation(rotation); }
                else shape.Copy.transform.SetPositionAndRotation(position, rotation);
                shape.AppliedPosition = position; shape.AppliedRotation = rotation;
            }
        }

        internal void Simulate(float dt) { physics.Simulate(dt); }

        internal Collider Mirror(Collider source)
        {
            if (!source) return null;
            Shape shape;
            if (copied.TryGetValue(source, out shape)) return shape.Copy;
            Copy(source);
            return copied.TryGetValue(source, out shape) ? shape.Copy : null;
        }

        public void Dispose()
        {
            foreach (Shape shape in shapes)
                if (shape.Copy && shape.Copy.sharedMaterial && shape.Copy.sharedMaterial != shape.Material)
                    UnityEngine.Object.Destroy(shape.Copy.sharedMaterial);
            if (environment) { environment.SetActive(false); UnityEngine.Object.Destroy(environment); }
            shapes.Clear();
            copied.Clear();
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
