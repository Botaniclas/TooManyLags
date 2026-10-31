using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TooManyLags;
using UnityEngine;

// Small managed scene seam. Exercises production hitbox geometry, ownership,
// rollback and restoration; does not claim to execute Unity physics queries.
namespace UnityEngine
{
    public partial class Object
    { public static void Destroy(Object value) { GameObject item = value as GameObject; if (item != null) item.Destroyed = true; } }
    public partial class GameObject
    {
        internal static readonly List<GameObject> Created = new List<GameObject>();
        public bool Destroyed; public int layer;
        public bool activeSelf { get { return activeInHierarchy; } }
        public GameObject(string name) : this() { Created.Add(this); }
        public T AddComponent<T>() where T : Component, new() { return Add<T>(); }
        public void SetActive(bool value) { activeInHierarchy = value; }
    }
    public partial class Component
    {
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component { return gameObject.GetComponentsInChildren<T>(inactive); }
        public T GetComponentInParent<T>() where T : Component
        {
            for (Transform item = transform; item != null; item = item.parent)
            { T value = item.gameObject.GetComponent<T>(); if (value != null) return value; }
            return null;
        }
    }
    public partial class Transform
    {
        public Vector3 localScale = new Vector3(1,1,1);
        public Vector3 lossyScale { get { return parent == null ? localScale : Scale(parent.lossyScale, localScale); } }
        private static Vector3 Scale(Vector3 a, Vector3 b) { return new Vector3(a.x*b.x,a.y*b.y,a.z*b.z); }
    }
    public partial class Rigidbody
    {
        public Vector3 position { get { return transform.position; } set { transform.position=value; } }
        public Quaternion rotation { get { return transform.rotation; } set { transform.rotation=value; } }
    }
    public partial class Collider
    { public bool enabled=true,isTrigger; public PhysicsMaterial sharedMaterial; public float contactOffset=.01f; }
    public class PhysicsMaterial : Object { }
    public class CapsuleCollider : Collider { public Vector3 center; public float radius=.5f,height=2; public int direction=1; }
    public class SphereCollider : Collider { public Vector3 center; public float radius=.5f; }
    public class BoxCollider : Collider { public Vector3 center,size; }
    public class Mesh : Object { }
    public class MeshCollider : Collider { public Mesh sharedMesh; public bool convex; public int cookingOptions; }
    public class UnknownCollider : Collider { }
    public class SkinnedMeshRenderer : Component { public bool forceMatrixRecalculationPerRender; }
    public static class LayerMask { public static int NameToLayer(string name) { return name == "hitbox" ? 15 : -1; } }
}
public partial class Character
{ public CapsuleCollider GetCollider() { return GetComponent<CapsuleCollider>(); } }
namespace TooManyLags
{
    internal sealed class MobBridgeAnimation
    {
        internal Transform Source;
        internal Vector3 Position, Scale;
        internal Quaternion Rotation;
        internal bool TryPose(Transform source, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        { position=Position; rotation=Rotation; scale=Scale; return source==Source; }
    }
    internal static class MobBridgePresentationPoint
    {
        internal static Vector3 TopPoint(Vector3 p, Quaternion q, Quaternion v, Vector3 c, float h, Vector3 s)
        { return p + q * (c + new Vector3(0,h/2,0)); }
    }
}
internal static class BridgeHitTests
{
    private static int checks;
    private static void Check(bool value, string message) { ++checks; if (!value) throw new Exception(message); }
    private static void Near(Vector3 a, Vector3 b, string message) { Check((a-b).magnitude < .0001f, message); }
    private static GameObject Child(GameObject parent)
    { GameObject result=new GameObject(); result.transform.parent=parent.transform; parent.transform.Children.Add(result.transform); return result; }
    private static Character Mob()
    {
        GameObject root=new GameObject(); Character actor=root.Add<Character>();
        root.Add<ZNetView>(); root.Add<Rigidbody>(); root.Add<CapsuleCollider>();
        actor.Visual=Child(root); return actor;
    }
    private static Collider Proxy(Collider source)
    {
        foreach (GameObject root in GameObject.Created.Where(g=>!g.Destroyed))
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                if (ReferenceEquals(MobBridgeHit.NativeCollider(source.GetComponentInParent<Character>(),collider),source)) return collider;
        throw new Exception("Missing proxy for " + source.GetType().Name);
    }
    private static void MultiCollider()
    {
        Character actor=Mob(); CapsuleCollider native=actor.GetCollider();
        actor.transform.position=new Vector3(10,0,0); actor.transform.rotation=Quaternion.AngleAxis(90,Vector3.up);
        native.direction=0; native.radius=.8f; native.height=4; native.center=new Vector3(1,2,3);
        SphereCollider head=Child(actor.Visual).Add<SphereCollider>();
        head.transform.localPosition=new Vector3(0,4,1); head.radius=.7f; head.center=new Vector3(0,.2f,0);
        head.transform.localScale=new Vector3(2,3,4); head.sharedMaterial=new PhysicsMaterial(); head.contactOffset=.03f;
        BoxCollider attack=Child(actor.gameObject).Add<BoxCollider>();
        attack.transform.localPosition=new Vector3(2,0,0); attack.size=new Vector3(1,2,3); attack.isTrigger=true;
        CapsuleCollider disabled=Child(actor.Visual).Add<CapsuleCollider>(); disabled.enabled=false;
        SphereCollider inactive=Child(actor.Visual).Add<SphereCollider>(); inactive.gameObject.SetActive(false);
        MeshCollider mesh=Child(actor.Visual).Add<MeshCollider>(); mesh.sharedMesh=new Mesh(); mesh.convex=true; mesh.cookingOptions=3;
        // Inactive ragdoll bodies under the visual no longer block admission.
        disabled.gameObject.Add<Rigidbody>();
        Character nested=Child(actor.gameObject).Add<Character>(); SphereCollider foreign=nested.gameObject.Add<SphereCollider>();
        GameObject nestedView=Child(actor.gameObject); nestedView.Add<ZNetView>(); BoxCollider independent=nestedView.Add<BoxCollider>();
        usingScope(actor,native,head,attack,disabled,inactive,mesh,foreign,independent);
    }
    private static void usingScope(Character actor, CapsuleCollider native, SphereCollider head, BoxCollider attack,
        CapsuleCollider disabled, SphereCollider inactive, MeshCollider mesh, Collider foreign, Collider independent)
    {
        Vector3 p=new Vector3(20,0,0); Quaternion q=Quaternion.identity;
        MobBridgeHit hit=MobBridgeHit.Create(actor,p,q);
        Check(hit!=null && hit.Count==6,"multi-collider mob rejected or nested entity included");
        CapsuleCollider rootProxy=(CapsuleCollider)Proxy(native);
        SphereCollider headProxy=(SphereCollider)Proxy(head);
        BoxCollider attackProxy=(BoxCollider)Proxy(attack);
        Collider disabledProxy=Proxy(disabled),inactiveProxy=Proxy(inactive);
        MeshCollider meshProxy=(MeshCollider)Proxy(mesh);
        Check(!native.enabled && !head.enabled && !attack.enabled && !mesh.enabled,"hidden native hitboxes still active");
        Check(foreign.enabled && independent.enabled,"nested entities changed");
        Check(rootProxy.direction==0 && rootProxy.radius==native.radius && rootProxy.height==native.height,"capsule dimensions/direction changed");
        Near(rootProxy.center,native.center,"capsule center changed");
        Check(headProxy.radius==head.radius && headProxy.sharedMaterial==head.sharedMaterial && headProxy.contactOffset==head.contactOffset,"sphere geometry/material changed");
        Near(headProxy.transform.lossyScale,head.transform.lossyScale,"nonuniform bone scale lost");
        Near(attackProxy.size,attack.size,"box geometry changed");
        Check(attackProxy.isTrigger,"trigger became solid");
        Check(!disabledProxy.enabled && !inactiveProxy.gameObject.activeInHierarchy,"disabled/inactive native shape became hittable");
        Check(meshProxy.convex==mesh.convex && meshProxy.cookingOptions==3 && meshProxy.sharedMesh==mesh.sharedMesh,"mesh geometry lost");
        Near(rootProxy.transform.position,p,"root proxy did not follow fake root");
        Near(attackProxy.transform.position,p+new Vector3(2,0,0),"nonvisual capsule offset/rotation wrong");
        // Production FollowHit applies the buffered bones before copying shapes.
        actor.Visual.transform.position=p; actor.Visual.transform.rotation=q;
        head.transform.localPosition=new Vector3(1,5,0);
        hit.Follow(p,q);
        Near(headProxy.transform.position,head.transform.position,"weak spot did not follow playback bone");
        Vector3 liveHead = head.transform.position;
        MobBridgeAnimation sample = new MobBridgeAnimation { Source=head.transform, Position=new Vector3(31,5,6),
            Rotation=Quaternion.AngleAxis(45,Vector3.up), Scale=new Vector3(3,4,5) };
        hit.Follow(p,q,sample);
        Near(headProxy.transform.position,sample.Position,"hitbox did not use prepared bone position");
        Near(headProxy.transform.lossyScale,sample.Scale,"hitbox did not use prepared bone scale");
        Near(head.transform.position,liveHead,"querying a bone pose changed the visible/native rig");
        Check(hit.Accepts(new HitData {m_hitCollider=headProxy}) && hit.HitsFakePose(new HitData {m_hitCollider=headProxy}),"weak spot hit rejected");
        Check(!hit.Accepts(new HitData {m_hitCollider=head}) && !hit.Accepts(new HitData {m_hitCollider=attack}),"hidden child hit accepted");
        Check(hit.Accepts(new HitData()) && hit.Accepts(new HitData {m_hitCollider=foreign}),"unrelated damage rejected");
        Check(MobBridgeHit.NativeCollider(actor,headProxy)==head,"native weak-spot identity lost");
        Check(MobBridgeHit.NativeCollider(foreign.GetComponentInParent<Character>(),headProxy)==headProxy,"cross-character weak-spot remapped");
        Character owner; Check(MobBridgeHit.TryOwner(headProxy,out owner) && owner==actor,"child query routed to wrong actor");
        foreach(Collider proxy in new Collider[]{rootProxy,headProxy,attackProxy,disabledProxy,inactiveProxy,meshProxy})
            Check(proxy.gameObject.layer==15 && proxy.GetComponent<Rigidbody>().isKinematic && proxy.transform.parent==null,"proxy linked to gameplay physics");
        hit.RestorePhysics();
        Check(native.enabled && head.enabled && attack.enabled && mesh.enabled && !disabled.enabled,"native enable states not restored");
        Check(!rootProxy.gameObject.activeInHierarchy && !headProxy.gameObject.activeInHierarchy,"proxy/native overlap after ownership returns");
        Check(hit.Accepts(new HitData {m_hitCollider=head}) && !hit.HitsFakePose(new HitData {m_hitCollider=headProxy}),"returned owner still uses preview hit filter");
        native.enabled=false; hit.RestorePhysics(); hit.Dispose(); hit.Dispose();
        Check(!native.enabled,"repeated cleanup overwrote new owner's native collider state");
        Check(!MobBridgeHit.TryOwner(headProxy,out owner),"proxy map leaked after dispose");
        Check(rootProxy.gameObject.Destroyed && meshProxy.gameObject.Destroyed,"proxy objects leaked");
    }
    private static void Rollback()
    {
        Character actor=Mob(); SphereCollider head=Child(actor.Visual).Add<SphereCollider>();
        Child(actor.Visual).Add<UnknownCollider>();
        bool failed=false;
        try { MobBridgeHit.Create(actor,new Vector3(),Quaternion.identity); }
        catch(NotSupportedException) {failed=true;}
        Check(failed && actor.GetCollider().enabled && head.enabled,"partial construction did not restore all native shapes");
        Character owner;
        Check(!GameObject.Created.Where(g=>!g.Destroyed).SelectMany(g=>g.GetComponentsInChildren<Collider>(true)).Any(c=>MobBridgeHit.TryOwner(c,out owner)),"partial construction leaked proxy mapping");
    }
    private static void SkinRestore()
    {
        GameObject visual=new GameObject();
        SkinnedMeshRenderer body=visual.Add<SkinnedMeshRenderer>();
        SkinnedMeshRenderer lod=Child(visual).Add<SkinnedMeshRenderer>(); lod.forceMatrixRecalculationPerRender=true;
        lod.gameObject.SetActive(false);
        MobBridgeSkin skin=new MobBridgeSkin(visual);
        Check(skin.Count==2 && skin.Changed==1 && body.forceMatrixRecalculationPerRender,
            "body/LOD renderers do not refresh overridden skin matrices");
        skin.Dispose();
        Check(!body.forceMatrixRecalculationPerRender && lod.forceMatrixRecalculationPerRender,
            "bridge cleanup changed original renderer settings");
        body.forceMatrixRecalculationPerRender=true; skin.Dispose();
        Check(body.forceMatrixRecalculationPerRender,"repeated skin cleanup overwrote a later renderer change");
    }
    public static int Main()
    {
        try { MultiCollider(); Rollback(); SkinRestore(); Console.WriteLine("PASS: "+checks+" multi-collider geometry/weak-spot/ownership/rollback/skin checks (managed scene seam)."); return 0; }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
