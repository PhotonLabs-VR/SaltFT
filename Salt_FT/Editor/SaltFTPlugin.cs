using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Smokey.SaltFT;

[assembly: ExportsPlugin(typeof(SaltFTPlugin))]

namespace Smokey.SaltFT
{
    /// <summary>
    /// Runs when you upload (or enter Play mode). Works on the temporary copy of the avatar only.
    /// </summary>
    public class SaltFTPlugin : Plugin<SaltFTPlugin>
    {
        public override string QualifiedName { get { return "smokey.salt-ft"; } }
        public override string DisplayName { get { return "Smokey's Salt Face Tracking"; } }

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq =>
                {
                    seq.Run("Salt face tracking: hand gesture faces off while tracking", ctx =>
                    {
                        bool hasMarker = ctx.AvatarRootObject.GetComponentsInChildren<SmokeySaltFT>(true).Length > 0;
                        var asc = ctx.Extension<AnimatorServicesContext>();
                        foreach (var ctrl in asc.ControllerContext.GetAllControllers())
                        {
                            // Also runs on avatars that carry this face tracking setup without the prefab marker.
                            if (!hasMarker && !ctrl.Layers.Any(l => l.Name == "Salt Lip Tracking Visemes")) continue;
                            foreach (var layer in ctrl.Layers.ToList())
                                DisableGestureFaces(layer);
                            AddFaceWow(ctrl);
                        }
                    });
                });

            InPhase(BuildPhase.Generating).Run("Add Salt face tracking shapes", ctx =>
            {
                var markers = ctx.AvatarRootObject.GetComponentsInChildren<SmokeySaltFT>(true);
                if (markers.Length == 0) return;
                var marker = markers[0];
                if (marker.patch == null) throw new InvalidOperationException("Smokey's Salt Face Tracking: the data file is missing on the prefab. Re-import the package.");

                var bytes = marker.patch.bytes;
                int baseCount, baseSub; ulong hash;
                if (!SaltFTPatchApplier.ReadHeader(bytes, out baseCount, out baseSub, out hash))
                    throw new InvalidOperationException("Smokey's Salt Face Tracking: the data file is damaged. Re-import the package.");

                var heads = new List<SkinnedMeshRenderer>();
                bool alreadyHasFT = false;
                foreach (var smr in ctx.AvatarRootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var m = smr.sharedMesh;
                    if (m == null) continue;
                    if (m.GetBlendShapeIndex("JawOpen") >= 0) alreadyHasFT = true;
                    else if (m.vertexCount == baseCount && m.subMeshCount == baseSub) heads.Add(smr);
                }
                if (heads.Count == 0)
                {
                    if (alreadyHasFT) { Debug.LogWarning("[Smokey's Salt Face Tracking] This avatar already has face tracking shapes on its head, so nothing was added."); return; }
                    throw new InvalidOperationException("Smokey's Salt Face Tracking: could not find the head mesh. This avatar's head must be the original, unedited Salt model.");
                }

                Mesh original = heads[0].sharedMesh;
                foreach (var h in heads)
                    if (h.sharedMesh != original) throw new InvalidOperationException("Smokey's Salt Face Tracking: found more than one matching head mesh, so I can't tell which is the head.");

                string error;
                var built = SaltFTPatchApplier.Apply(original, bytes, out error);
                if (built == null) throw new InvalidOperationException("Smokey's Salt Face Tracking: " + error);
                built.name = original.name + "_FT";
                ctx.AssetSaver.SaveAsset(built);

                foreach (var h in heads)
                {
                    if (h.bones.Length != built.bindposes.Length)
                        throw new InvalidOperationException("Smokey's Salt Face Tracking: the bones on this avatar don't line up with the head mesh.");
                    h.sharedMesh = built;
                    // Leftover weights from an older setup can sit in slots past the original shapes.
                    // Those slots are now real face tracking shapes, so make sure they start at 0.
                    for (int i = original.blendShapeCount; i < built.blendShapeCount; i++) h.SetBlendShapeWeight(i, 0f);
                }
            });
        }

        // Salt's left / right hand layers play a full-face expression for each hand gesture.
        // Gate them on FacialExpressionsDisabled, which the face tracking menu sets while tracking is on.
        static void DisableGestureFaces(VirtualLayer layer)
        {
            if (layer.Name != "Left Hand" && layer.Name != "Right Hand") return;
            var sm = layer.StateMachine;
            if (sm == null) return;
            VirtualState idle = null; bool hasFist = false, hasRock = false;
            foreach (var s in sm.AllStates())
            {
                if (s.Name == "Idle") idle = s;
                if (s.Name == "Fist_1") hasFist = true;
                if (s.Name == "RockNRoll_2") hasRock = true;
            }
            if (idle == null || !hasFist || !hasRock) return;
            foreach (var t in sm.AnyStateTransitions)
            {
                foreach (var c in t.Conditions) if (c.parameter == "FacialExpressionsDisabled") return; // already done
                t.Conditions = t.Conditions.Add(new AnimatorCondition { mode = AnimatorConditionMode.IfNot, parameter = "FacialExpressionsDisabled", threshold = 0f });
            }
            var off = VirtualStateTransition.Create();
            off.SetDestination(idle);
            off.Conditions = off.Conditions.Add(new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "FacialExpressionsDisabled", threshold = 0f });
            off.HasFixedDuration = true; off.Duration = 0f; off.ExitTime = null; off.CanTransitionToSelf = false;
            sm.AnyStateTransitions = sm.AnyStateTransitions.Insert(0, off);
        }

        // 'Face wow' menu toggle: plays the face of Salt's thumbs-up gesture (Salt's own animation,
        // looked up on the avatar at upload time, so nothing of the model is copied into this package).
        static void AddFaceWow(VirtualAnimatorController ctrl)
        {
            VirtualMotion m1 = null, m2 = null;
            foreach (var layer in ctrl.Layers)
            {
                if (layer.Name != "Left Hand" || layer.StateMachine == null) continue;
                foreach (var s in layer.StateMachine.AllStates())
                {
                    if (s.Name == "Thumbs up_1") m1 = s.Motion;
                    if (s.Name == "Thumbs up_2") m2 = s.Motion;
                }
            }
            if (m1 == null) return;
            if (ctrl.Layers.Any(l => l.Name == "Salt Face Wow")) return;
            if (m2 == null) m2 = m1;

            ctrl.SetParameter("FaceWow", new AnimatorControllerParameter { name = "FaceWow", type = AnimatorControllerParameterType.Bool, defaultBool = false });
            if (!ctrl.Parameters.ContainsKey("FacialSet"))
                ctrl.SetParameter("FacialSet", new AnimatorControllerParameter { name = "FacialSet", type = AnimatorControllerParameterType.Int, defaultInt = 0 });

            var wow = ctrl.AddLayer(LayerPriority.Default, "Salt Face Wow");
            wow.DefaultWeight = 1f;
            wow.BlendingMode = AnimatorLayerBlendingMode.Override;
            var sm = wow.StateMachine;
            var idle = sm.AddState("Idle", null, null);
            var w1 = sm.AddState("Face Wow 1", m1, null);
            var w2 = sm.AddState("Face Wow 2", m2, null);
            idle.WriteDefaultValues = true; w1.WriteDefaultValues = true; w2.WriteDefaultValues = true;
            sm.DefaultState = idle;

            sm.AnyStateTransitions = sm.AnyStateTransitions
                .Add(WowTransition(idle, new AnimatorCondition { mode = AnimatorConditionMode.IfNot, parameter = "FaceWow", threshold = 0f }))
                .Add(WowTransition(w1,
                    new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "FaceWow", threshold = 0f },
                    new AnimatorCondition { mode = AnimatorConditionMode.Equals, parameter = "FacialSet", threshold = 0f }))
                .Add(WowTransition(w2,
                    new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "FaceWow", threshold = 0f },
                    new AnimatorCondition { mode = AnimatorConditionMode.Equals, parameter = "FacialSet", threshold = 1f }));
        }

        static VirtualStateTransition WowTransition(VirtualState to, params AnimatorCondition[] conditions)
        {
            var t = VirtualStateTransition.Create();
            t.SetDestination(to);
            foreach (var c in conditions) t.Conditions = t.Conditions.Add(c);
            t.HasFixedDuration = true; t.Duration = 0.1f; t.ExitTime = null; t.CanTransitionToSelf = false;
            return t;
        }
    }

    [CustomEditor(typeof(SmokeySaltFT))]
    public class SmokeySaltFTEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Smokey's Salt Face Tracking\n\n" +
                "Face tracking shapes are added to a temporary copy of the head when you upload or press Play. " +
                "Your model, mesh and avatar are not changed.",
                MessageType.Info);
        }
    }
}
