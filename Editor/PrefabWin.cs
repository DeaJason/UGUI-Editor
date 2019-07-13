using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace U3DExtends
{
    public class PrefabWin : EditorWindow
    {
        [MenuItem("Window/PrefabWin", false, 9)]
        static public void OpenPrefabTool()
        {
            PrefabWin prefabWin = (PrefabWin)EditorWindow.GetWindow<PrefabWin>(false, "Prefab Win", true);
            prefabWin.autoRepaintOnSceneChange = true;
            prefabWin.Show();
        }

        static public PrefabWin instance;
        private static int s_pageBarOption = 0;
        private static string[] s_pageBarTexts = null;

        private static string newPageBarName = "";
        string grayGuiStyle = "flow node 0";
        string GreenGuiStyle = "flow node 6";

        class Item
        {
            public GameObject prefab;
            public string guid;
            public Texture tex;
            public bool dynamicTex = false;
            public bool isDirty = false;

            public Item()
            {
                isDirty = false;
            }
        }

        enum Mode
        {
            CompactMode,
            IconMode,
            DetailedMode,
        }

        const int cellPadding = 4;
        float mSizePercent = 0.5f;

        public float SizePercent
        {
            get { return mSizePercent; }
            set
            {
                if (mSizePercent != value)
                {
                    mReset = true;
                    mSizePercent = value;
                    mCellSize = Mathf.FloorToInt(80 * SizePercent + 10);
                    EditorPrefs.SetFloat("PrefabWin_SizePercent", mSizePercent);
                }
            }
        }
        int mCellSize = 50;
        int cellSize { get { return mCellSize; } }

        int mTab = 0;
        //Mode mMode = Mode.DetailedMode;
        Mode m_mode = Mode.DetailedMode;
        Mode mMode
        {
            set { m_mode = value; Debug.LogError("SetMode"); }
            get { return m_mode; }
        }
        Vector2 mPos = Vector2.zero;
        bool mMouseIsInside = false;
        GUIContent mContent;
        GUIStyle mStyle;

        BetterList<Item> mItems = new BetterList<Item>();

        GameObject draggedObject
        {
            get
            {
                if (DragAndDrop.objectReferences == null) return null;
                if (DragAndDrop.objectReferences.Length == 1) return DragAndDrop.objectReferences[0] as GameObject;
                return null;
            }
            set
            {
                if (value != null)
                {
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new Object[1] { value };
                    draggedObjectIsOurs = true;
                }
                else DragAndDrop.AcceptDrag();
            }
        }

        bool draggedObjectIsOurs
        {
            get
            {
                object obj = DragAndDrop.GetGenericData("Prefab Tool");
                if (obj == null) return false;
                return (bool)obj;
            }
            set
            {
                DragAndDrop.SetGenericData("Prefab Tool", value);
            }
        }

        void OnEnable()
        {
            instance = this;
            RefreshToolBarPages();
            Load();

            mContent = new GUIContent();
            mStyle = new GUIStyle();
            mStyle.alignment = TextAnchor.MiddleCenter;
            mStyle.padding = new RectOffset(2, 2, 2, 2);
            mStyle.clipping = TextClipping.Clip;
            mStyle.wordWrap = true;
            mStyle.stretchWidth = false;
            mStyle.stretchHeight = false;
            mStyle.normal.textColor = UnityEditor.EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.5f) : new Color(0f, 0f, 0f, 0.5f);
            mStyle.normal.background = null;
        }

        void OnDisable()
        {
            SaveTextureToPng();
            instance = null;
            foreach (Item item in mItems) DestroyTexture(item);
            Save();
        }

        void OnSelectionChange() { Repaint(); }

        public void Reset()
        {
            foreach (Item item in mItems) DestroyTexture(item);
            mItems.Clear();

            if (mTab == 0 && Configure.PrefabWinFirstSearchPath != "")
            {
                List<string> filtered = new List<string>();
                string[] allAssets = AssetDatabase.GetAllAssetPaths();

                foreach (string s in allAssets)
                {
                    //search prefab files in folder:Configure.PrefabWinFirstSearchPath
                    bool isComeFromPrefab = Regex.IsMatch(s, Configure.PrefabWinFirstSearchPath + @"/((?!/).)*\.prefab");
                    if (isComeFromPrefab)
                        filtered.Add(s);
                }

                filtered.Sort(string.Compare);
                foreach (string s in filtered) AddGUID(AssetDatabase.AssetPathToGUID(s), -1);
                RectivateLights();
            }
        }

        void AddItem(GameObject go, int index)
        {
            string guid = U3DExtends.UIEditorHelper.ObjectToGUID(go);

            if (string.IsNullOrEmpty(guid))
            {
                string path = EditorUtility.SaveFilePanelInProject("Save a prefab",
                    go.name + ".prefab", "prefab", "Save prefab as...", "");

                if (string.IsNullOrEmpty(path)) return;

                go = PrefabUtility.CreatePrefab(path, go);
                if (go == null) return;

                guid = U3DExtends.UIEditorHelper.ObjectToGUID(go);
                if (string.IsNullOrEmpty(guid)) return;
            }

            Item ent = new Item();
            ent.prefab = go;
            ent.guid = guid;
            GeneratePreview(ent);
            RectivateLights();

            if (index < mItems.size) mItems.Insert(index, ent);
            else mItems.Add(ent);
            Save();
        }

        Item AddGUID(string guid, int index)
        {
            GameObject go = U3DExtends.UIEditorHelper.GUIDToObject<GameObject>(guid);

            if (go != null)
            {
                Item ent = new Item();
                ent.prefab = go;
                ent.guid = guid;
                GeneratePreview(ent, false);
                if (index < mItems.size) mItems.Insert(index, ent);
                else mItems.Add(ent);
                return ent;
            }
            return null;
        }

        void RemoveItem(object obj)
        {
            if (this == null) return;
            int index = (int)obj;
            if (index < mItems.size && index > -1)
            {
                Item item = mItems[index];
                DestroyTexture(item);
                mItems.RemoveAt(index);
            }
            Save();
        }

        Item FindItem(GameObject go)
        {
            for (int i = 0; i < mItems.size; ++i)
                if (mItems[i].prefab == go)
                    return mItems[i];
            return null;
        }
        void SaveTextureToPng()
        {
            for (int i = 0; i < mItems.size; i++)
            {
                Item item = mItems[i];
                if (item == null || item.prefab == null || item.tex == null || item.isDirty == false)
                {
                    continue;
                }
                UIEditorHelper.SaveTextureToPNG(item.tex, GetPreviewPath(item));
                string preview_path = GetPreviewPath(item);
                item.tex = UIEditorHelper.LoadTextureInLocal(preview_path);
                item.isDirty = false;
            }
            GameObject root = GameObject.Find(Configure.PreviewCanvasName);
            if (root)
            {
                DestroyImmediate(root);
            }
        }

        int GetCurrSelectTab()
        {
            return mTab;
        }

        string GetCurrSelectPageName()
        {
            if (s_pageBarTexts.Length < GetCurrSelectTab())
            {
                return s_pageBarTexts[0];
            }
            return s_pageBarTexts[GetCurrSelectTab()];
        }

        string GetPreviewPath(Item item) 
        {
            string prefabName = "TestPrefab";
            if (item == null || item.prefab == null)
            {
                Debug.LogError("item is null or item.prefab is null");
            }
            else
            {
                prefabName = item.prefab.name;
            }
            string pageName = GetCurrSelectPageName();
            string preview_path = Configure.PrefabPreviewResAssetsPath + "/" + pageName + "/" + prefabName + ".png";
            return preview_path;
        }

        string GetGuidsFromLocal()
        {
            string filePath = saveKey;
            if (!File.Exists(filePath))
            {
                return "";
            }
            StreamReader sr = File.OpenText(saveKey);
            string guids = sr.ReadToEnd();
            sr.Close();
            return guids;
        }

        void SaveGuidsToLocal(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return;
            }
            File.WriteAllText(saveKey,data);
        }
        string saveKey
        {
            get
            {
                string pageName = GetCurrSelectPageName();
                string guids_path = Configure.PrefabPreviewResAssetsPath + "/" + pageName + "/" + pageName + ".txt";
                return guids_path;
            }
        }

        void Save()
        {
            string data = "";

            if (mItems.size > 0)
            {
                string guid = mItems[0].guid;
                StringBuilder sb = new StringBuilder();
                sb.Append(guid);

                for (int i = 1; i < mItems.size; ++i)
                {
                    guid = mItems[i].guid;

                    if (string.IsNullOrEmpty(guid))
                    {
                        Debug.LogWarning("Unable to save " + mItems[i].prefab.name);
                    }
                    else
                    {
                        sb.Append('|');
                        sb.Append(mItems[i].guid);
                    }
                }
                data = sb.ToString();
            }
            //EditorPrefs.SetString(saveKey, data);
            SaveGuidsToLocal(data);
        }

        void Load()
        {
            mTab = EditorPrefs.GetInt("PrefabWin Prefab Tab", 0);
            SizePercent = EditorPrefs.GetFloat("PrefabWin_SizePercent", 0.5f);

            foreach (Item item in mItems) DestroyTexture(item);
            mItems.Clear();

            //string data = EditorPrefs.GetString(saveKey, "");
            string data = GetGuidsFromLocal();
            //data = "";//For test
            if (string.IsNullOrEmpty(data))
            {
                Reset();
            }
            else
            {
                if (string.IsNullOrEmpty(data)) return;
                string[] guids = data.Split('|');
                foreach (string s in guids) AddGUID(s, -1);
                RectivateLights();
            }
        }

        void DestroyTexture(Item item)
        {
            if (item != null && item.dynamicTex && item.tex != null)
            {
                DestroyImmediate(item.tex);
                item.dynamicTex = false;
                item.tex = null;
            }
        }

        void UpdateVisual()
        {
            if (draggedObject == null) DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            else if (draggedObjectIsOurs) DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            else DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }

        Item CreateItemByPath(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                path = FileUtil.GetProjectRelativePath(path);
                string guid = AssetDatabase.AssetPathToGUID(path);

                if (!string.IsNullOrEmpty(guid))
                {
                    GameObject go = AssetDatabase.LoadAssetAtPath(path, typeof(GameObject)) as GameObject;
                    Item ent = new Item();
                    ent.prefab = go;
                    ent.guid = guid;
                    GeneratePreview(ent);
                    return ent;
                }
                else Debug.Log("No GUID");
            }
            return null;
        }

        void GeneratePreview(Item item, bool isReCreate = true)
        {
            if (item == null || item.prefab == null) return;
            {
                //string preview_path = Configure.ResAssetsPath + "/Preview/" + item.prefab.name + ".png";

                string preview_path = GetPreviewPath(item);
                if (!isReCreate && File.Exists(preview_path))
                {
                    Texture texture = UIEditorHelper.LoadTextureInLocal(preview_path);
                    item.tex = texture;
                    item.isDirty = false;
                }
                else
                {
                    Texture Tex = UIEditorHelper.GetAssetPreview(item.prefab);
                    if (Tex != null)
                    {
                        DestroyTexture(item);
                        item.tex = Tex;
                        item.isDirty = true;//需要保存png图片
                        //UIEditorHelper.SaveTextureToPNG(Tex, preview_path);
                    }
                }
                item.dynamicTex = false;
                return;
            }
        }

        static Transform FindChild(Transform t, string startsWith)
        {
            if (t.name.StartsWith(startsWith)) return t;

            for (int i = 0, imax = t.childCount; i < imax; ++i)
            {
                Transform ch = FindChild(t.GetChild(i), startsWith);
                if (ch != null) return ch;
            }
            return null;
        }

        static BetterList<Light> mLights;

        static void RectivateLights()
        {
            if (mLights != null)
            {
                for (int i = 0; i < mLights.size; ++i)
                    mLights[i].enabled = true;
                mLights = null;
            }
        }

        int GetCellUnderMouse(int spacingX, int spacingY)
        {
            Vector2 pos = Event.current.mousePosition + mPos;

            int topPadding = 24;
            int x = cellPadding, y = cellPadding + topPadding;
            if (pos.y < y) return -1;

            float width = Screen.width - cellPadding + mPos.x;
            float height = Screen.height - cellPadding + mPos.y;
            int index = 0;

            for (; ; ++index)
            {
                Rect rect = new Rect(x, y, spacingX, spacingY);
                if (rect.Contains(pos)) break;

                x += spacingX;

                if (x + spacingX > width)
                {
                    if (pos.x > x) return -1;
                    y += spacingY;
                    x = cellPadding;
                    if (y + spacingY > height) return -1;
                }
            }
            return index;
        }

        void RefreshToolBarPages()
        {

            string prePageName = "";
            if (s_pageBarTexts != null)
            {
                prePageName = s_pageBarTexts[s_pageBarOption];
            }
            s_pageBarTexts = Directory.GetDirectories(Configure.PrefabPreviewResAssetsPath);
            string pageName = "";
            for (int i = 0; i < s_pageBarTexts.Length; i++)
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(s_pageBarTexts[i]);
                pageName = directoryInfo.Name;
                s_pageBarTexts[i] = pageName;
                if (pageName == prePageName)
                {
                    s_pageBarOption = i;
                }
            }
        }

        bool mReset = false;

        void TopButton()
        {
            if (GUILayout.Button("保存模板",GUILayout.Height(20f)))
            {
                SaveTextureToPng();
            }
        }

        void ToolBar()
        {
            GUILayout.Label("模板分页：", EditorStyles.boldLabel, GUILayout.Height(20f));
            GUILayout.BeginVertical();
            s_pageBarOption = GUILayout.Toolbar(s_pageBarOption, s_pageBarTexts, GUILayout.Height(20f));
            GUILayout.BeginHorizontal();
            GUILayout.Space(4);
            string guiStyle = "flow node 0";
            if (!string.IsNullOrEmpty(newPageBarName))
            {
                guiStyle = "flow node 3";
            }
            if (GUILayout.Button("创建分页", guiStyle, GUILayout.Width(90f), GUILayout.Height(30f)))
            {
                AddPageBar(newPageBarName);
                newPageBarName = "";
            }
            newPageBarName = EditorGUILayout.TextField(newPageBarName, GUILayout.Height(20f));
            if (GUILayout.Button("删除分页", "flow node 5", GUILayout.Width(90f), GUILayout.Height(30f)))
            {
                RemovePageBar();
            }
            GUILayout.Space(4);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        void AddPageBar(string pageBarName)
        {
            if (string.IsNullOrEmpty(pageBarName))
            {
                return;
            }
            string fullName = Configure.PrefabPreviewResAssetsPath + "/" + pageBarName;
            if (!Directory.Exists(fullName))
            {
                Directory.CreateDirectory(fullName);
                RefreshToolBarPages();
            }

        }
        void RemovePageBar()
        {
            if (EditorUtility.DisplayDialog("warning", "你要删除模板分页？", "ok"))
            {
                string fullName = Configure.PrefabPreviewResAssetsPath + "/" + GetCurrSelectPageName();
                if (Directory.Exists(fullName))
                {
                    Directory.Delete(fullName,true);
                    RefreshToolBarPages();
                    if (s_pageBarTexts.Length < s_pageBarOption)
                    {
                        s_pageBarOption = s_pageBarTexts.Length;
                    }
                }
            }
        }

        void OnGUI()
        {
            Event currentEvent = Event.current;
            EventType type = currentEvent.type;

            int x = cellPadding, y = cellPadding;
            int width = Screen.width - cellPadding;
            int spacingX = cellSize + cellPadding;
            int spacingY = spacingX;
            if (mMode == Mode.DetailedMode) spacingY += 32;

            GameObject dragged = draggedObject;
            bool isDragging = (dragged != null);
            int indexUnderMouse = GetCellUnderMouse(spacingX, spacingY);
            Item selection = isDragging ? FindItem(dragged) : null;
            string searchFilter = EditorPrefs.GetString("PrefabWin_SearchFilter", null);
            s_pageBarOption = mTab;
            TopButton();
            ToolBar();

            if (mTab != s_pageBarOption)
            {
                Save();
                SaveTextureToPng();
                mTab = s_pageBarOption;
                mReset = true;
                EditorPrefs.SetInt("PrefabWin Prefab Tab", mTab);
                Load();
            }

            if (mReset && type == EventType.Repaint)
            {
                mReset = false;
                SaveTextureToPng();
                foreach (Item item in mItems) GeneratePreview(item, false);
                RectivateLights();
            }
            bool eligibleToDrag = (currentEvent.mousePosition.y < Screen.height - 40 && currentEvent.mousePosition.y > 110);

            if (type == EventType.MouseDown)
            {
                mMouseIsInside = true;
            }
            else if (type == EventType.MouseDrag)
            {
                mMouseIsInside = true;

                if (indexUnderMouse != -1 && eligibleToDrag)
                {
                    if (draggedObjectIsOurs) DragAndDrop.StartDrag("Prefab Tool");
                    currentEvent.Use();
                }
            }
            else if (type == EventType.MouseUp)
            {
                DragAndDrop.PrepareStartDrag();
                mMouseIsInside = false;
                Repaint();
            }
            else if (type == EventType.DragUpdated)
            {
                mMouseIsInside = true;
                UpdateVisual();
                currentEvent.Use();
            }
            else if (type == EventType.DragPerform)
            {
                if (dragged != null)
                {
                    if (selection != null)
                    {
                        DestroyTexture(selection);
                        mItems.Remove(selection);
                    }

                    AddItem(dragged, indexUnderMouse);
                    draggedObject = null;
                }
                mMouseIsInside = false;
                currentEvent.Use();
            }
            else if (type == EventType.DragExited || type == EventType.Ignore)
            {
                mMouseIsInside = false;
            }

            if (!mMouseIsInside)
            {
                selection = null;
                dragged = null;
            }

            BetterList<int> indices = new BetterList<int>();

            for (int i = 0; i < mItems.size;)
            {
                if (dragged != null && indices.size == indexUnderMouse)
                    indices.Add(-1);

                if (mItems[i] != selection)
                {
                    if (string.IsNullOrEmpty(searchFilter) ||
                        mItems[i].prefab.name.IndexOf(searchFilter, System.StringComparison.CurrentCultureIgnoreCase) != -1)
                        indices.Add(i);
                }
                ++i;
            }

            if (!indices.Contains(-1)) indices.Add(-1);

            if (eligibleToDrag && type == EventType.MouseDown && indexUnderMouse > -1)
            {
                GUIUtility.keyboardControl = 0;

                if (currentEvent.button == 0 && indexUnderMouse < indices.size)
                {
                    int index = indices[indexUnderMouse];

                    if (index != -1 && index < mItems.size)
                    {
                        selection = mItems[index];
                        draggedObject = selection.prefab;
                        dragged = selection.prefab;
                        currentEvent.Use();
                    }
                }
            }

            mPos = EditorGUILayout.BeginScrollView(mPos);
            {
                Color normal = new Color(1f, 1f, 1f, 0.5f);
                for (int i = 0; i < indices.size; ++i)
                {
                    int index = indices[i];
                    Item ent = (index != -1) ? mItems[index] : selection;

                    if (ent != null && ent.prefab == null)
                    {
                        mItems.RemoveAt(index);
                        continue;
                    }

                    Rect rect = new Rect(x, y, cellSize, cellSize);
                    Rect inner = rect;
                    inner.xMin += 2f;
                    inner.xMax -= 2f;
                    inner.yMin += 2f;
                    inner.yMax -= 2f;
                    rect.yMax -= 1f;

                    if (!isDragging && (mMode == Mode.CompactMode || (ent == null || ent.tex != null)))
                    {
                        mContent.tooltip = (ent != null) ? ent.prefab.name : "Click to add";
                    }
                    else mContent.tooltip = "";

                    //if (ent == selection)
                    {
                        GUI.color = normal;
                        U3DExtends.UIEditorHelper.DrawTiledTexture(inner, U3DExtends.UIEditorHelper.backdropTexture);
                    }

                    GUI.color = Color.white;
                    GUI.backgroundColor = normal;

                    if (GUI.Button(rect, mContent, "Button"))
                    {
                        if (ent == null || currentEvent.button == 0)
                        {
                            string path = EditorUtility.OpenFilePanel("Add a prefab", "", "prefab");

                            if (!string.IsNullOrEmpty(path))
                            {
                                Item newEnt = CreateItemByPath(path);

                                if (newEnt != null)
                                {
                                    mItems.Add(newEnt);
                                    Save();
                                }
                            }
                        }
                        else if (currentEvent.button == 1)
                        {
                            //ContextMenu.AddItem("Update Preview", false, UpdatePreView, index);
                            ContextMenu.AddItemWithArge("Delete", false, RemoveItem, index);
                            ContextMenu.Show();
                        }
                    }

                    string caption = (ent == null) ? "" : ent.prefab.name.Replace("Control - ", "");

                    if (ent != null)
                    {
                        if (ent.tex == null)
                        {
                            //texture may be destroy after exit game
                            GeneratePreview(ent, false);
                        }
                        if (ent.tex != null)
                        {
                            GUI.DrawTexture(inner, ent.tex, ScaleMode.ScaleToFit, false);
                        }
                        else if (mMode != Mode.DetailedMode)
                        {
                            GUI.Label(inner, caption, mStyle);
                            caption = "";
                        }
                    }
                    else GUI.Label(inner, "Add", mStyle);

                    if (mMode == Mode.DetailedMode)
                    {
                        GUI.backgroundColor = new Color(1f, 1f, 1f, 0.5f);
                        GUI.contentColor = new Color(1f, 1f, 1f, 0.7f);
                        string guistyle = "flow node 0";
                        if (ent != null && ent.isDirty)
                        {
                            guistyle = "flow node 3";
                        }
                        GUI.Label(new Rect(rect.x, rect.y + rect.height, rect.width, 32f), caption, guistyle);
                        GUI.contentColor = Color.white;
                        GUI.backgroundColor = Color.white;
                    }

                    x += spacingX;

                    if (x + spacingX > width)
                    {
                        y += spacingY;
                        x = cellPadding;
                    }
                }
                GUILayout.Space(y + spacingY);
            }
            EditorGUILayout.EndScrollView();
            //if (mTab == 0)
            {
                //EditorGUILayout.BeginHorizontal();
                //bool isCreateBackground = GUILayout.Button("背景");
                //if (isCreateBackground)
                //    EditorApplication.ExecuteMenuItem("UIEditor/创建/Background");

                //bool isCreateDecorate = GUILayout.Button("参考图");
                //if (isCreateDecorate)
                //    EditorApplication.ExecuteMenuItem("UIEditor/创建/Decorate");
                //EditorGUILayout.EndHorizontal();
            }
            //else if (mTab != 0)
            {
                GUILayout.BeginHorizontal();
                {
                    string after = EditorGUILayout.TextField("", searchFilter, "SearchTextField", GUILayout.Width(Screen.width - 20f));

                    if (GUILayout.Button("", "SearchCancelButton", GUILayout.Width(18f)))
                    {
                        after = "";
                        GUIUtility.keyboardControl = 0;
                    }

                    if (searchFilter != after)
                    {
                        EditorPrefs.SetString("PrefabWin_SearchFilter", after);
                        searchFilter = after;
                    }
                }
                GUILayout.EndHorizontal();
            }

            SizePercent = EditorGUILayout.Slider(SizePercent, 0, 10);
        }
    }
}