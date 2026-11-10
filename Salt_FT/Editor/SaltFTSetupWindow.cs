using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Smokey.SaltFT
{
    public class SaltFTSetupWindow : EditorWindow
    {
        GameObject avatar;
        Vector2 scroll;

        bool custom;

        [MenuItem("Tools/Smokey/Salt/Custom Avatar Patcher", false, 101)]
        static void OpenCustom() { Open(true, "Custom Avatar Patcher"); }

        static void Open(bool custom, string title)
        {
            var w = CreateInstance<SaltFTSetupWindow>();
            w.custom = custom;
            w.titleContent = new GUIContent(title);
            w.minSize = new Vector2(340, 300);
            w.Show();
        }

        static GameObject FindPrefab()
        {
            foreach (var g in AssetDatabase.FindAssets("Smokey Salt Face Tracking t:Prefab"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null && go.GetComponent<SmokeySaltFT>() != null) return go;
            }
            return null;
        }

        struct Check { public List<string> problems; public List<string> warnings; public bool ok; public bool added; }

        Check Inspect(GameObject root, TextAsset patch)
        {
            var c = new Check { problems = new List<string>(), warnings = new List<string>() };
            if (root.GetComponentInChildren<SmokeySaltFT>(true) != null) { c.added = true; return c; }
            if (patch == null) { c.problems.Add("The patch data file is missing from the package."); return c; }

            int baseCount, baseSub; ulong hash;
            try { SaltFTPatchApplier.ReadHeader(patch.bytes, out baseCount, out baseSub, out hash); }
            catch (System.Exception) { c.problems.Add("The patch data file is damaged. Reimport the package."); return c; }

            int found = 0; bool already = false; int sameCount = 0; bool patched = false;
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = r.sharedMesh; if (m == null) continue;
                if (m.vertexCount == baseCount && m.subMeshCount == baseSub)
                {
                    sameCount++;
                    if (SaltFTPatchCommon.MeshHash(m) == hash)
                    {
                        found++;
                        for (int i = 0; i < m.blendShapeCount; i++) if (m.GetBlendShapeName(i) == "JawOpen") { already = true; break; }
                    }
                }
                else if (m.GetBlendShapeIndex("JawOpen") >= 0 && m.GetBlendShapeIndex("TongueOut") >= 0)
                {
                    patched = true;
                }
            }
            if (found == 0 && patched)
                c.warnings.Add("This avatar already has the face tracking shapes. Only the menu and animations will be added.");
            else if (found == 0)
                c.problems.Add(sameCount > 0
                    ? "A body mesh was found, but it is not the untouched original Salt body (it was edited or re-imported differently). Face tracking can only be added to the original body mesh."
                    : "The original Salt body mesh was not found on this avatar.");
            else if (found > 1) c.problems.Add("More than one original body mesh was found. Remove the duplicate first.");
            if (already) c.problems.Add("This body mesh already has face tracking shapes.");

            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                var n = comp.GetType().FullName;
                bool ftSetup = false;
                if (!ftSetup && n.Contains("VRCFury"))
                {
                    // VRCFury is a general tool (GogoLoco, write defaults, ...). Only count it if it holds face tracking.
                    string json = null;
                    try { json = EditorJsonUtility.ToJson(comp); } catch (System.Exception) { }
                    if (json != null && (json.Contains("VRCFT") || json.Contains("JawOpen") || json.Contains("FT/v2"))) ftSetup = true;
                }
                if (ftSetup)
                { c.warnings.Add("Another face tracking setup (" + comp.gameObject.name + ") is on this avatar. Remove it first or the two will clash."); break; }
            }
            foreach (Transform t in root.transform)
                if (t.name.Contains("VRCFT"))
                    c.warnings.Add("'" + t.name + "' looks like an older face tracking setup. Remove it first.");

            c.ok = c.problems.Count == 0;
            return c;
        }

        void Apply(GameObject target, GameObject prefab)
        {
            // Run after the GUI event, not inside it, so the window layout never changes mid-draw.
            EditorApplication.delayCall += () =>
            {
                if (target == null || prefab == null) return;
                if (target.GetComponentInChildren<SmokeySaltFT>(true) != null) return;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, target.transform);
                Undo.RegisterCreatedObjectUndo(inst, "Add Salt Face Tracking");
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                Selection.activeGameObject = inst;
                if (this != null) Repaint();
            };
        }

        void DrawStatus(Check chk)
        {
            if (chk.added) { EditorGUILayout.HelpBox("Face tracking is added to this avatar.", MessageType.Info); return; }
            foreach (var p in chk.problems) EditorGUILayout.HelpBox(p, MessageType.Error);
            foreach (var w in chk.warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
            if (chk.ok && chk.warnings.Count == 0) EditorGUILayout.HelpBox("Ready. Click Apply.", MessageType.None);
        }

        void OnGUI()
        {
            var prefab = FindPrefab();
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(custom ? "Custom Avatar Patcher" : "FT Patcher", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Adds face tracking to your Salt. Nothing in your model is changed. The shapes are added to a temporary copy when you upload or press Play.", MessageType.Info);
            EditorGUILayout.Space(6);
            if (prefab == null) { EditorGUILayout.HelpBox("The face tracking prefab was not found in the package.", MessageType.Error); return; }
            var patch = prefab.GetComponent<SmokeySaltFT>().patch;

            if (custom)
            {
                EditorGUILayout.LabelField("Your avatar here");
                avatar = (GameObject)EditorGUILayout.ObjectField(avatar, typeof(GameObject), true);
                if (avatar != null && EditorUtility.IsPersistent(avatar))
                {
                    EditorGUILayout.HelpBox("Drag the avatar from the Hierarchy (the one in your scene), not from the Project window.", MessageType.Warning);
                    return;
                }
                Check chk = default(Check);
                if (avatar != null)
                {
                    chk = Inspect(avatar, patch);
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    DrawStatus(chk);
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUI.DisabledScope(avatar == null || !chk.ok))
                    if (GUILayout.Button("Apply", GUILayout.Height(30))) Apply(avatar, prefab);
            }
            else
            {
                EditorGUILayout.LabelField("Avatars in the open scene");
                scroll = EditorGUILayout.BeginScrollView(scroll);
                int shown = 0;
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (root.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>() == null) continue;
                    shown++;
                    var chk = Inspect(root, patch);
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField(root.name, EditorStyles.boldLabel);
                    DrawStatus(chk);
                    using (new EditorGUI.DisabledScope(!chk.ok))
                        if (GUILayout.Button("Apply", GUILayout.Height(24))) Apply(root, prefab);
                    EditorGUILayout.EndVertical();
                }
                if (shown == 0) EditorGUILayout.HelpBox("No avatars found in the open scene. Put your Salt in the scene, or use Custom Avatar Patcher.", MessageType.Info);
                EditorGUILayout.EndScrollView();
            }
        }
    }
}
