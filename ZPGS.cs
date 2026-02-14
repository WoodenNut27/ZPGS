using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

public class ZPGS : EditorWindow
{
    private GameObject targetObject;
    private GameObject targetObjectTmp;
    private List<bool> physSelected = new List<bool>();
    private Vector2 physScrollPos = Vector2.zero;
    private int selectedSerializedIndex = 0;

    private string version = "1.0.0";

    [MenuItem("Tools/ZPGS")]
    public static void ShowWindow()
    {
        GetWindow<ZPGS>("ZPGS");
    }

    // GUIウィンドウの内容を定義
    private void OnGUI()
    {
        GUILayout.Label("雑にPhysBoneのグラブ設定をするやつ - ver " + version, EditorStyles.boldLabel);

        // 対象のゲームオブジェクトを選択するフィールド
        targetObject = (GameObject)EditorGUILayout.ObjectField("対象オブジェクト", targetObject, typeof(GameObject), true);

        // ゲームオブジェクトが選択されたら
        if (targetObject != null)
        {
            // 選択されたゲームオブジェクトの子オブジェクトを再帰的に探索して、VRC Phys Bone (Script) コンポーネントを持つゲームオブジェクトを配列に格納
            var physBoneList = targetObject.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(mb => {
                    var fn = mb.GetType().FullName;
                    if (fn == null) return false;
                    // 型名に "Collider" を含むコンポーネントは除外
                    if (fn.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
                    return fn.StartsWith("VRC.SDK3.Dynamics.PhysBone", System.StringComparison.Ordinal);
                })
                .Select(mb => mb.gameObject)
                .Distinct()
                .ToList();

            // 対象オブジェクトが変更された場合は選択リストをリセット
            if (targetObject != targetObjectTmp)
            {
                physSelected = new List<bool>(new bool[physBoneList.Count]);
                targetObjectTmp = targetObject;
            }

            // physSelected のサイズを physBoneList に合わせる（既存の選択を保持）
            if (physSelected.Count != physBoneList.Count)
            {
                var newList = new List<bool>(physBoneList.Count);
                for (int i = 0; i < physBoneList.Count; i++)
                {
                    newList.Add(i < physSelected.Count ? physSelected[i] : false);
                }
                physSelected = newList;
            }

            // スクロール可能な複数選択リスト（境界が分かるようにボックスで囲む）
            GUILayout.BeginVertical(GUI.skin.box);
            physScrollPos = GUILayout.BeginScrollView(physScrollPos, GUILayout.Height(this.position.height - 300));
            for (int i = 0; i < physBoneList.Count; i++)
            {
                var obj = physBoneList[i];
                physSelected[i] = GUILayout.Toggle(physSelected[i], GetGameObjectPath(obj), EditorStyles.toggle);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // 全選択/全解除のボタン
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全選択"))
            {
                for (int i = 0; i < physSelected.Count; i++)
                {
                    physSelected[i] = true;
                }
            }
            if (GUILayout.Button("全解除"))
            {
                for (int i = 0; i < physSelected.Count; i++)
                {
                    physSelected[i] = false;
                }
            }
            GUILayout.EndHorizontal();

            // 選択された数
            var selectedCount = physSelected.Count(b => b);

            if (selectedCount > 0)
            {
                // 選択ステータス(-1=未処理/0=無効/1=有効/3=Other)
                int grabStatus = -1;
                int poseStatus = -1;

                // 選択されたPhysBoneのIsGrabbedとIsPosedの現在の状態を取得
                foreach (var obj in physBoneList.Where((obj, index) => physSelected[index]))
                {
                    var physBone = obj.GetComponent<MonoBehaviour>();
                    var physBoneSerialized = new SerializedObject(physBone);

                    // allowGrabbingとallowPosingのプロパティを探して状態を確認
                    // allowGrabbingはEnumで、False=0, True=1、Other=2
                    // allowPosingも同様
                    var grabProp = physBoneSerialized.FindProperty("allowGrabbing");
                    if (grabProp != null)
                    {
                        if (grabStatus == -1)
                        {
                            grabStatus = grabProp.enumValueIndex;
                        }
                        else if (grabStatus != grabProp.enumValueIndex)
                        {
                            grabStatus = 4; // 不一致の場合は4に設定
                        }
                    }

                    var poseProp = physBoneSerialized.FindProperty("allowPosing");
                    if (poseProp != null)
                    {
                        if (poseStatus == -1)
                        {
                            poseStatus = poseProp.enumValueIndex;
                        }
                        else if (poseStatus != poseProp.enumValueIndex)
                        {
                            poseStatus = 4; // 不一致の場合は4に設定
                        }
                    }

                    if (grabStatus == 4 && poseStatus == 4)
                    {
                        break; // 両方とも不一致ならこれ以上確認する必要はない
                    }
                }

                // 状態に応じた選択肢のテキストとインデックス(現状はOtherは実装してないけど一応残してる)
                string[] grabSelectedText = { "False", "True"/*, "Other"*/ };
                string[] poseSelectedText = { "False", "True"/*, "Other"*/ };
                int grabSelectedIndex = -1;
                int poseSelectedIndex = -1;
                bool grabOtherAllowSelf = false;
                bool grabOtherAllowOther = false;
                bool poseOtherAllowSelf = false;
                bool poseOtherAllowOther = false;

                // 状態に応じて選択肢のインデックスを設定
                if (grabStatus >= 0 && grabStatus <= 1)
                {
                    grabSelectedIndex = grabStatus;
                }

                if (poseStatus >= 0 && poseStatus <= 1)
                {
                    poseSelectedIndex = poseStatus;
                }

                // まとめて設定するためのラジオボタン（左: ラベル、右: 選択）
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(this.position.width * 0.5f));
                GUILayout.Label("Grab設定", EditorStyles.boldLabel);
                GUILayout.EndVertical();

                GUILayout.BeginVertical();
                int grabSelectedIndexTmp = GUILayout.SelectionGrid(grabSelectedIndex, grabSelectedText, 1, EditorStyles.radioButton);

                // Other を選んだ時に表示するチェックボックス
                if (grabSelectedIndex == 2)
                {
                    grabOtherAllowSelf = EditorGUILayout.Toggle("Allow Self", grabOtherAllowSelf);
                    grabOtherAllowOther = EditorGUILayout.Toggle("Allow Others", grabOtherAllowOther);
                }
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(this.position.width * 0.5f));
                GUILayout.Label("Pose設定", EditorStyles.boldLabel);
                GUILayout.EndVertical();

                GUILayout.BeginVertical();
                int poseSelectedIndexTmp = GUILayout.SelectionGrid(poseSelectedIndex, poseSelectedText, 1, EditorStyles.radioButton);

                if (poseSelectedIndex == 2)
                {
                    poseOtherAllowSelf = EditorGUILayout.Toggle("Allow Self", poseOtherAllowSelf);
                    poseOtherAllowOther = EditorGUILayout.Toggle("Allow Others", poseOtherAllowOther);
                }
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();

                if ((grabSelectedIndexTmp != grabSelectedIndex && grabSelectedIndexTmp != -1) || (poseSelectedIndexTmp != poseSelectedIndex && poseSelectedIndexTmp != -1))
                {
                    for (int i = 0; i < physBoneList.Count; i++)
                    {
                        if (physSelected[i])
                        {
                            var obj = physBoneList[i];
                            var physBone = obj.GetComponent<MonoBehaviour>();
                            var physBoneSerialized = new SerializedObject(physBone);

                            var grabProp = physBoneSerialized.FindProperty("allowGrabbing");
                            if (grabProp != null && grabSelectedIndexTmp != -1)
                            {
                                grabProp.enumValueIndex = grabSelectedIndexTmp;
                            }

                            var poseProp = physBoneSerialized.FindProperty("allowPosing");
                            if (poseProp != null && poseSelectedIndexTmp != -1)
                            {
                                poseProp.enumValueIndex = poseSelectedIndexTmp;
                            }

                            physBoneSerialized.ApplyModifiedProperties();
                        }
                    }
                }
            }
            /*
            // Debug用： 選択されたPhysBoneのプロパティを表示
            int debugIndex = 0; // 最初のPhysBoneをデバッグ表示
            GUILayout.Label("Debug: 個別のPhysBoneのプロパティを表示", EditorStyles.boldLabel);
            debugIndex = EditorGUILayout.Popup(debugIndex, physBoneList.Select((obj, index) => GetGameObjectPath(obj)).ToArray(), EditorStyles.popup);

            if (debugIndex >= 0 && debugIndex < physBoneList.Count)
            {
                DebugViewComponentProperties(physBoneList[debugIndex].GetComponent<MonoBehaviour>());
            }*/
        }
    }

    // ゲームオブジェクトの階層パスを取得するヘルパーメソッド
    private string GetGameObjectPath(GameObject obj)
    {
        if (obj == null) return string.Empty;
        string path = obj.name;
        Transform t = obj.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }

    // デバッグ用： MonoBehaviourのプロパティを表示するメソッド
    private void DebugViewComponentProperties(MonoBehaviour mb)
    {
        GUILayout.Label($"Component: {mb.GetType().FullName}");

        var type = mb.GetType();
        var props = type.GetProperties();
        string[] reflectionList = props.Select(p => p.Name).ToArray();
        int selectedReflectionIndex = 0;
        selectedReflectionIndex = EditorGUILayout.Popup("ReflectionProperty",selectedReflectionIndex, reflectionList);

        if (selectedReflectionIndex >= 0 && selectedReflectionIndex < props.Length)
        {
            var selectedProp = props[selectedReflectionIndex];
            object value = null;
            try
            {
                value = selectedProp.GetValue(mb, null);
            }
            catch { }
            GUILayout.Label($"Selected Property: {selectedProp.Name}, Value: {value}");
        }

        var so = new SerializedObject(mb);
        var it = so.GetIterator();
        var serializedList = new List<string>();

        if (it.Next(true))
        {
            do
            {
                serializedList.Add(it.propertyPath);
            } while (it.Next(false));
        }

        if (serializedList.Count > 0)
        {
            selectedSerializedIndex = Mathf.Clamp(selectedSerializedIndex, 0, serializedList.Count - 1);
            selectedSerializedIndex = EditorGUILayout.Popup("SerializedProperty", selectedSerializedIndex, serializedList.ToArray());

            var selPath = serializedList[selectedSerializedIndex];
            var selProp = so.FindProperty(selPath);
            if (selProp != null)
            {
                EditorGUILayout.LabelField("Selected Serialized Property:");
                switch (selProp.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        bool b = selProp.boolValue;
                        bool nb = EditorGUILayout.Toggle(selProp.propertyPath, b);
                        if (nb != b) { selProp.boolValue = nb; so.ApplyModifiedProperties(); }
                        break;
                    case SerializedPropertyType.Integer:
                        int iv = selProp.intValue;
                        int niv = EditorGUILayout.IntField(selProp.propertyPath, iv);
                        if (niv != iv) { selProp.intValue = niv; so.ApplyModifiedProperties(); }
                        break;
                    case SerializedPropertyType.Float:
                        float fv = selProp.floatValue;
                        float nfv = EditorGUILayout.FloatField(selProp.propertyPath, fv);
                        if (nfv != fv) { selProp.floatValue = nfv; so.ApplyModifiedProperties(); }
                        break;
                    case SerializedPropertyType.String:
                        string sv = selProp.stringValue;
                        string nsv = EditorGUILayout.TextField(selProp.propertyPath, sv);
                        if (nsv != sv) { selProp.stringValue = nsv; so.ApplyModifiedProperties(); }
                        break;
                    case SerializedPropertyType.Enum:
                        int eidx = selProp.enumValueIndex;
                        int neid = EditorGUILayout.Popup(selProp.propertyPath, eidx, selProp.enumNames);
                        if (neid != eidx) { selProp.enumValueIndex = neid; so.ApplyModifiedProperties(); }
                        break;
                    case SerializedPropertyType.ObjectReference:
                        var ov = selProp.objectReferenceValue;
                        var nov = EditorGUILayout.ObjectField(selProp.propertyPath, ov, typeof(Object), true);
                        if (nov != ov) { selProp.objectReferenceValue = nov; so.ApplyModifiedProperties(); }
                        break;
                    default:
                        EditorGUILayout.LabelField("(unsupported type)");
                        break;
                }
            }
        }
    }
}
