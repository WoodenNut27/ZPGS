using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

/// <summary>
/// PhysBoneSetting クラス: Grab / Pose 共通の設定状態と操作をまとめたクラス
/// </summary>
public class PhysBoneSetting
{
    public readonly string label;               // "Grab" / "Pose"
    public readonly string allowPropertyName;   // "allowGrabbing" / "allowPosing"
    public readonly string filterPropertyName;  // "grabFilter" / "poseFilter"

    public int selectedIndex = -1;
    public bool otherAllowSelf;
    public bool otherAllowOthers;
    public bool otherInitialized;

    // ステータス読み取り用（毎フレーム再計算）
    // -2=プロパティなし / -1=未処理 / 0=無効 / 1=有効 / 2=Other / 3=不一致
    public int status;
    public int otherAllowSelfStatus;
    public int otherAllowOthersStatus;

    private static readonly string[] Options = { "False", "True", "Other" };

    /// <summary>
    /// PhysBoneSetting のコンストラクタ
    /// </summary>
    /// <param name="label">設定のラベル（例: "Grab"）</param>
    /// <param name="allowPropertyName">PhysBone のプロパティ名（例: "allowGrabbing"）</param>
    /// <param name="filterPropertyName">PhysBone Filter のプロパティ名（例: "grabFilter"）</param>
    public PhysBoneSetting(string label, string allowPropertyName, string filterPropertyName)
    {
        this.label = label;
        this.allowPropertyName = allowPropertyName;
        this.filterPropertyName = filterPropertyName;
    }

    /// <summary>
    /// 設定をリセット（対象オブジェクトが変更されたときなどに呼び出す）
    /// </summary>
    public void Reset()
    {
        selectedIndex = -1;
        otherAllowSelf = false;
        otherAllowOthers = false;
        otherInitialized = false;
    }

    /// <summary>
    /// ステータス読み取りの開始時にリセット
    /// </summary>
    public void BeginReadStatus()
    {
        status = -1;
        otherAllowSelfStatus = -1;
        otherAllowOthersStatus = -1;
    }

    /// <summary>
    /// 1つの SerializedObject からステータスを蓄積
    /// </summary>
    /// <param name="so">SerializedObject</param>
    public void ReadStatus(SerializedObject so)
    {
        var prop = so.FindProperty(allowPropertyName);
        if (prop == null)
        {
            if (status == -1) status = -2;
            return;
        }

        status = AccumulateEnumStatus(status, prop.enumValueIndex);

        if (prop.enumValueIndex == 2)
        {
            var filterProp = so.FindProperty(filterPropertyName);
            if (filterProp != null)
            {
                otherAllowSelfStatus = AccumulateBoolPropStatus(otherAllowSelfStatus, filterProp.FindPropertyRelative("allowSelf"));
                otherAllowOthersStatus = AccumulateBoolPropStatus(otherAllowOthersStatus, filterProp.FindPropertyRelative("allowOthers"));
            }
            else
            {
                if (otherAllowSelfStatus == -1) otherAllowSelfStatus = -2;
                if (otherAllowOthersStatus == -1) otherAllowOthersStatus = -2;
            }
        }
    }

    /// <summary>
    /// ステータスから GUI のインデックスと詳細設定を初期化（初回のみ）
    /// </summary>
    public void InitializeFromStatus()
    {
        if (selectedIndex < 0 && status >= 0 && status <= 2)
            selectedIndex = status;

        if (!otherInitialized)
        {
            otherAllowSelf = otherAllowSelfStatus == 1;
            otherAllowOthers = otherAllowOthersStatus == 1;
            otherInitialized = true;
        }
    }

    /// <summary>
    /// GUI カラムを描画（BeginVertical 〜 EndVertical を含む）
    /// </summary>
    /// <param name="physBoneList">PhysBone のリスト</param>
    /// <param name="physSelected">選択状態のリスト</param>
    /// <param name="width">カラムの幅</param>
    public void DrawGUI(List<GameObject> physBoneList, List<bool> physSelected, float width)
    {
        GUILayout.BeginVertical(GUILayout.Width(width));

        GUILayout.Label(label + "設定", EditorStyles.boldLabel);
        selectedIndex = GUILayout.SelectionGrid(selectedIndex, Options, 1, EditorStyles.radioButton);

        if (selectedIndex == 2)
        {
            otherAllowSelf = GUILayout.Toggle(otherAllowSelf, "Allow Self");
            otherAllowOthers = GUILayout.Toggle(otherAllowOthers, "Allow Others");
        }

        if (GUILayout.Button("PhysBoneに" + label + "設定を反映"))
        {
            ApplyToPhysBones(physBoneList, physSelected);
        }

        GUILayout.EndVertical();
    }

    /// <summary>
    /// 警告メッセージを描画
    /// </summary>
    public void DrawWarnings()
    {
        if (status == -2)
            EditorGUILayout.HelpBox("Allow " + label + " プロパティが見つかりませんでした。", MessageType.Warning);
        if (otherAllowSelfStatus == -2)
            EditorGUILayout.HelpBox(label + " Filter の Allow Self プロパティが見つかりませんでした。", MessageType.Warning);
        if (otherAllowOthersStatus == -2)
            EditorGUILayout.HelpBox(label + " Filter の Allow Others プロパティが見つかりませんでした。", MessageType.Warning);
    }

    /// <summary>
    /// 選択された PhysBone に設定を反映
    /// </summary>
    /// <param name="physBoneList">PhysBone のリスト</param>
    /// <param name="physSelected">選択状態のリスト</param>
    private void ApplyToPhysBones(List<GameObject> physBoneList, List<bool> physSelected)
    {
        foreach (var obj in physBoneList.Where((o, i) => physSelected[i]))
        {
            var physBone = obj.GetComponent<MonoBehaviour>();
            var so = new SerializedObject(physBone);

            var prop = so.FindProperty(allowPropertyName);
            if (prop != null && selectedIndex >= 0)
            {
                prop.enumValueIndex = selectedIndex;
            }

            if (selectedIndex == 2)
            {
                var filterProp = so.FindProperty(filterPropertyName);
                if (filterProp != null)
                {
                    var selfProp = filterProp.FindPropertyRelative("allowSelf");
                    var othersProp = filterProp.FindPropertyRelative("allowOthers");
                    if (selfProp != null) selfProp.boolValue = otherAllowSelf;
                    if (othersProp != null) othersProp.boolValue = otherAllowOthers;
                }
            }

            so.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// enum ステータスの蓄積: 不一致なら 3
    /// </summary>
    /// <param name="current">現在のステータス</param>
    /// <param name="value">新しいステータス</param>
    /// <returns>蓄積後のステータス</returns>
    private static int AccumulateEnumStatus(int current, int value)
    {
        if (current == -1) return value;
        if (current != value) return 3;
        return current;
    }

    /// <summary>
    /// bool プロパティのステータス蓄積: null なら -2、不一致なら 2
    /// </summary>
    /// <param name="current">現在のステータス</param>
    /// <param name="prop">SerializedProperty</param>
    /// <returns>蓄積後のステータス</returns>
    private static int AccumulateBoolPropStatus(int current, SerializedProperty prop)
    {
        if (prop == null)
        {
            return current == -1 ? -2 : current;
        }
        int intVal = prop.boolValue ? 1 : 0;
        if (current == -1) return intVal;
        if (current != intVal) return 2;
        return current;
    }
}

/// <summary>
/// ZPGS のメインエディタウィンドウクラス
/// </summary>
public class ZPGS : EditorWindow
{
    private GameObject targetObject;
    private GameObject targetObjectTmp;
    private List<bool> physSelected = new List<bool>();
    private Vector2 physScrollPos = Vector2.zero;
    private bool flgViweFullPath = false;
    private string version = "1.1.0";

    private PhysBoneSetting grabSetting = new PhysBoneSetting("Grab", "allowGrabbing", "grabFilter");
    private PhysBoneSetting poseSetting = new PhysBoneSetting("Pose", "allowPosing", "poseFilter");
    private static ZPGS window;

    [MenuItem("Tools/ZPGS")]
    public static void ShowWindow()
    {
        window = GetWindow<ZPGS>("ZPGS");
        window.minSize = new Vector2(350, 450);
    }

    /// <summary>
    /// GUI の描画
    /// </summary>
    private void OnGUI()
    {
        GUILayout.Label("雑にPhysBoneのグラブ設定をするやつ - ver " + version, EditorStyles.boldLabel);

        targetObject = (GameObject)EditorGUILayout.ObjectField("対象オブジェクト", targetObject, typeof(GameObject), true);

        if (targetObject != null)
        {
            var physBoneList = targetObject.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(mb => {
                    var fn = mb.GetType().FullName;
                    if (fn == null) return false;
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
                physScrollPos = Vector2.zero;
                grabSetting.Reset();
                poseSetting.Reset();
                targetObjectTmp = targetObject;
            }

            // physSelected のサイズを physBoneList に合わせる
            if (physSelected.Count != physBoneList.Count)
            {
                var newList = new List<bool>(physBoneList.Count);
                for (int i = 0; i < physBoneList.Count; i++)
                {
                    newList.Add(i < physSelected.Count ? physSelected[i] : false);
                }
                physSelected = newList;
            }

            // フルパス表示のトグル
            flgViweFullPath = EditorGUILayout.Toggle("フルパス表示", flgViweFullPath);

            // スクロール可能な複数選択リスト
            GUILayout.BeginVertical(GUI.skin.box);
            physScrollPos = GUILayout.BeginScrollView(physScrollPos, GUILayout.Height(this.position.height - 400));
            for (int i = 0; i < physBoneList.Count; i++)
            {
                string displayName = flgViweFullPath ? GetGameObjectPath(physBoneList[i]) : physBoneList[i].name;
                physSelected[i] = GUILayout.Toggle(physSelected[i], displayName, EditorStyles.toggle);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // 全選択/全解除
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全選択"))
            {
                for (int i = 0; i < physSelected.Count; i++) physSelected[i] = true;
            }
            if (GUILayout.Button("全解除"))
            {
                for (int i = 0; i < physSelected.Count; i++) physSelected[i] = false;
            }
            GUILayout.EndHorizontal();

            var selectedCount = physSelected.Count(b => b);

            if (selectedCount > 0)
            {
                // 選択された PhysBone からステータスを読み取り
                grabSetting.BeginReadStatus();
                poseSetting.BeginReadStatus();

                foreach (var obj in physBoneList.Where((obj, index) => physSelected[index]))
                {
                    var so = new SerializedObject(obj.GetComponent<MonoBehaviour>());
                    grabSetting.ReadStatus(so);
                    poseSetting.ReadStatus(so);

                    if (grabSetting.status == 3 && poseSetting.status == 3)
                        break;
                }

                grabSetting.InitializeFromStatus();
                poseSetting.InitializeFromStatus();

                // Grab と Pose を左右に並べる
                GUILayout.BeginHorizontal();
                grabSetting.DrawGUI(physBoneList, physSelected, position.width * 0.5f);
                poseSetting.DrawGUI(physBoneList, physSelected, position.width * 0.5f);
                GUILayout.EndHorizontal();

                // 警告メッセージ
                grabSetting.DrawWarnings();
                poseSetting.DrawWarnings();
            }
        }
    }

    /// <summary>
    /// GameObject の階層パスを取得
    /// </summary>
    /// <param name="obj">GameObject</param>
    /// <returns>階層パス（例: "Root/Child/SubChild"）</returns>
    private string GetGameObjectPath(GameObject obj)
    {
        if (obj == null) return string.Empty;

        // 階層を遡ってパスを構築
        string path = obj.name;
        Transform t = obj.transform.parent;

        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        
        return path;
    }
}
