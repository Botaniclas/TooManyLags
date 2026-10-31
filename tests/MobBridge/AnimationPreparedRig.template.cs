using System;
using System.Collections.Generic;
using UnityEngine;
namespace TooManyLags
{
    internal sealed class @@NAME@@ : IPreparedRig
    {
        private readonly Transform[] visible;
        private readonly Vector3[] positions, scales, savedPositions, savedScales;
        private readonly Quaternion[] rotations, savedRotations;
        private readonly Dictionary<Transform,int> poseIndices=new Dictionary<Transform,int>();
        private readonly MobBridgeAnimationHistory History;
        private Matrix4x4 modelMatrix;private Quaternion modelRotation;
        private float realWeight;private bool applied;
        public int Samples { get; private set; }
        internal @@NAME@@(Transform[] rig,MobBridgeAnimationHistory history)
        {
            visible=rig;History=history;int n=rig.Length;
            positions=new Vector3[n];scales=new Vector3[n];rotations=new Quaternion[n];
            savedPositions=new Vector3[n];savedScales=new Vector3[n];savedRotations=new Quaternion[n];
            for(int i=0;i<n;++i)poseIndices.Add(rig[i],i);
        }
        private void CountSample(float time) { Samples++;History.Sample(time,positions,rotations,scales); }
@@METHODS@@
    }
}
