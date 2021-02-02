using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.Extensions;
using UILib;
using IllusionPlugin;
using System.IO;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Xml;
using Config;
using Harmony;
using Studio;
using System.Reflection;

// imitate windows explorer thumbnail spacing and positioning for scene loader
// reset hsstudioaddon lighting on load if no xml data
// problem adjusting thumbnail size when certain number range of scenes
// indicator if scene has mod xml attached

namespace BetterSceneLoader
{
    public class BetterSceneLoader : MonoBehaviour
    {
        static string scenePath = Environment.CurrentDirectory + "/UserData/studioneo/BetterSceneLoader/";
        static string orderPath = scenePath + "order.txt";
        static bool bslSaving = false;
        const int screenshotWidth = 1280;
        const int screenshotHeight = 720;

        float buttonSize = 10f;
        float marginSize = 5f;
        float headerSize = 20f;
        float UIScale = 1.0f;
        float scrollOffsetX = -15f;
        float windowMargin = 130f;

        Color dragColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        Color backgroundColor = new Color(1f, 1f, 1f, 1f);
        Color outlineColor = new Color(0f, 0f, 0f, 1f);

        Canvas UISystem;
        Image mainPanel;
        Dropdown category;
        ScrollRect imagelist;
        Image optionspanel;
        Image confirmpanel;
        Button yesbutton;
        Button nobutton;
        Text nametext;
        static Camera tagCamera;
        static Canvas tagCanvas;
        InputField tagInputField;
        static Text tagText;
        static Material overlayMat;

        int columnCount;
        bool useExternalSavedata;
        float scrollSensitivity;
        bool autoClose;
        bool smallWindow;

        Dictionary<string, Image> sceneCache = new Dictionary<string, Image>();
        Button currentButton;
        string currentPath;

        void Awake()
        {
            HSExtSave.HSExtSave.RegisterHandler("betterSceneLoader", null, null, HSExtSaveSceneLoad, null, HSExtSaveSceneSave, null, null);

            HarmonyInstance harmony = HarmonyInstance.Create("betterSceneLoader");
            harmony.Patch(AccessTools.Method(typeof(SceneInfo), "CreatePngScreen"), new HarmonyMethod(typeof(BetterSceneLoader), nameof(SceneInfoCreatePngScreenPrefix)));

            UIUtility.Init();
            MakeBetterSceneLoader();
            LoadSettings();
            StartCoroutine(StartingScene());
        }

        IEnumerator StartingScene()
        {
            for(int i = 0; i < 10; i++) yield return null;
            var files = Directory.GetFiles(scenePath, "defaultscene.png", SearchOption.TopDirectoryOnly).ToList();
            if(files.Count > 0) LoadScene(files[0]);
        }

        void OnDestroy()
        {
            DestroyImmediate(UISystem.gameObject);
        }

        bool LoadSettings()
        {
            columnCount = ModPrefs.GetInt("BetterSceneLoader", "ColumnCount", 3, true);
            useExternalSavedata = ModPrefs.GetBool("BetterSceneLoader", "UseExternalSavedata", true, true);
            scrollSensitivity = ModPrefs.GetFloat("BetterSceneLoader", "ScrollSensitivity", 3f, true);
            autoClose = ModPrefs.GetBool("BetterSceneLoader", "AutoClose", true, true);
            smallWindow = ModPrefs.GetBool("BetterSceneLoader", "SmallWindow", true, true);

            UpdateWindow();
            return true;
        }

        void UpdateWindow()
        {
            foreach(var scene in sceneCache)
            {
                var gridlayout = scene.Value.gameObject.GetComponent<AutoGridLayout>();
                if(gridlayout != null)
                {
                    gridlayout.m_Column = columnCount;
                    gridlayout.CalculateLayoutInputHorizontal();
                }
            }

            if(imagelist != null)
            {
                imagelist.scrollSensitivity = Mathf.Lerp(30f, 300f, scrollSensitivity / 10f);
            }

            if(mainPanel)
            {
                if(smallWindow)
                    mainPanel.transform.SetRect(0.5f, 0f, 1f, 1f, windowMargin, windowMargin, -windowMargin, -windowMargin);
                else
                    mainPanel.transform.SetRect(0f, 0f, 1f, 1f, windowMargin, windowMargin, -windowMargin, -windowMargin); 
            }
        }

        void MakeBetterSceneLoader()
        {
            UISystem = UIUtility.CreateNewUISystem("BetterSceneLoaderCanvas");
            UISystem.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f / UIScale, 1080f / UIScale);
            UISystem.gameObject.SetActive(false);
            UISystem.gameObject.transform.SetParent(transform);

            mainPanel = UIUtility.CreatePanel("Panel", UISystem.transform);
            mainPanel.color = backgroundColor;
            UIUtility.AddOutlineToObject(mainPanel.transform, outlineColor);

            var drag = UIUtility.CreatePanel("Draggable", mainPanel.transform);
            drag.transform.SetRect(0f, 1f, 1f, 1f, 0f, -headerSize);
            drag.color = dragColor;
            UIUtility.MakeObjectDraggable(drag.rectTransform, mainPanel.rectTransform);

            nametext = UIUtility.CreateText("Nametext", drag.transform, "Scenes");
            nametext.transform.SetRect(0f, 0f, 1f, 1f, 340f, 0f, -buttonSize * 2f);
            nametext.alignment = TextAnchor.MiddleCenter;

            var close = UIUtility.CreateButton("CloseButton", drag.transform, "");
            close.transform.SetRect(1f, 0f, 1f, 1f, -buttonSize * 2f);
            close.onClick.AddListener(() => UISystem.gameObject.SetActive(false));
            Utils.AddCloseSymbol(close);

            category = UIUtility.CreateDropdown("Dropdown", drag.transform, "Categories");
            category.transform.SetRect(0f, 0f, 0f, 1f, 0f, 0f, 100f);
            category.captionText.transform.SetRect(0f, 0f, 1f, 1f, 0f, 2f, -15f, -2f);
            category.captionText.alignment = TextAnchor.MiddleCenter;
            category.options = GetCategories();
            category.onValueChanged.AddListener((x) =>
            {
                imagelist.content.GetComponentInChildren<Image>().gameObject.SetActive(false);
                imagelist.content.anchoredPosition = new Vector2(0f, 0f);
                PopulateGrid();
            });

            var refresh = UIUtility.CreateButton("RefreshButton", drag.transform, "Refresh");
            refresh.transform.SetRect(0f, 0f, 0f, 1f, 100f, 0f, 180f);
            refresh.onClick.AddListener(() => ReloadImages());

            var save = UIUtility.CreateButton("SaveButton", drag.transform, "Save");
            save.transform.SetRect(0f, 0f, 0f, 1f, 180f, 0f, 260f);
            save.onClick.AddListener(() => SaveScene());

            var folder = UIUtility.CreateButton("FolderButton", drag.transform, "Folder");
            folder.transform.SetRect(0f, 0f, 0f, 1f, 260f, 0f, 340f);
            folder.onClick.AddListener(() => Process.Start(scenePath));

            tagInputField = UIUtility.CreateInputField("TagFiels", drag.transform, "Tag...");
            tagInputField.transform.SetRect(0f, 0f, 0f, 1f, 340f, 0f, 500f);
            tagInputField.onEndEdit.AddListener((s) => SetTag(tagInputField.text.Trim()));

            var loadingPanel = UIUtility.CreatePanel("LoadingIconPanel", drag.transform);
            loadingPanel.transform.SetRect(0f, 0f, 0f, 1f, 500f, 0f, 500f + headerSize);
            loadingPanel.color = new Color(0f, 0f, 0f, 0f);
            var loadingIcon = UIUtility.CreatePanel("LoadingIcon", loadingPanel.transform);
            loadingIcon.transform.SetRect(0.1f, 0.1f, 0.9f, 0.9f);
            var texture = Utils.LoadTexture(Properties.Resources.loadicon);
            loadingIcon.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            LoadingIcon.Init(loadingIcon, -5f);

            imagelist = UIUtility.CreateScrollView("Imagelist", mainPanel.transform);
            imagelist.transform.SetRect(0f, 0f, 1f, 1f, marginSize, marginSize, -marginSize, -headerSize - marginSize / 2f);
            imagelist.gameObject.AddComponent<Mask>();
            imagelist.content.gameObject.AddComponent<VerticalLayoutGroup>();
            imagelist.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            imagelist.verticalScrollbar.GetComponent<RectTransform>().offsetMin = new Vector2(scrollOffsetX, 0f);
            imagelist.viewport.offsetMax = new Vector2(scrollOffsetX, 0f);
            imagelist.movementType = ScrollRect.MovementType.Clamped;

            optionspanel = UIUtility.CreatePanel("ButtonPanel", imagelist.transform);
            optionspanel.gameObject.SetActive(false);

            confirmpanel = UIUtility.CreatePanel("ConfirmPanel", imagelist.transform);
            confirmpanel.gameObject.SetActive(false);

            yesbutton = UIUtility.CreateButton("YesButton", confirmpanel.transform, "Y");
            yesbutton.transform.SetRect(0f, 0f, 0.5f, 1f);
            yesbutton.onClick.AddListener(() => DeleteScene(currentPath));

            nobutton = UIUtility.CreateButton("NoButton", confirmpanel.transform, "N");
            nobutton.transform.SetRect(0.5f, 0f, 1f, 1f);
            nobutton.onClick.AddListener(() => confirmpanel.gameObject.SetActive(false));

            var loadbutton = UIUtility.CreateButton("LoadButton", optionspanel.transform, "Load");
            loadbutton.transform.SetRect(0f, 0f, 0.3f, 1f);
            loadbutton.onClick.AddListener(() => LoadScene(currentPath));

            var importbutton = UIUtility.CreateButton("ImportButton", optionspanel.transform, "Import");
            importbutton.transform.SetRect(0.35f, 0f, 0.65f, 1f);
            importbutton.onClick.AddListener(() => ImportScene(currentPath));

            var deletebutton = UIUtility.CreateButton("DeleteButton", optionspanel.transform, "Delete");
            deletebutton.transform.SetRect(0.7f, 0f, 1f, 1f);
            deletebutton.onClick.AddListener(() => confirmpanel.gameObject.SetActive(true));

            PopulateGrid();

            tagCamera = new GameObject("TagCamera").AddComponent<Camera>();
            tagCamera.transform.SetParent(transform);
            tagCamera.cullingMask = LayerMask.GetMask("UI");
            tagCamera.clearFlags = CameraClearFlags.SolidColor;
            tagCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            tagCamera.depth = 80;
            //tagCamera.enabled = false;

            tagCanvas = UIUtility.CreateNewUISystem("TagOverlayCanvas");
            Destroy(tagCanvas.GetComponent<GraphicRaycaster>());
            tagCanvas.gameObject.layer = 5;
            tagCanvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(screenshotWidth, screenshotHeight);
            tagCanvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            tagCanvas.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            tagCanvas.gameObject.transform.SetParent(transform);
            tagCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            tagCanvas.worldCamera = tagCamera;
            tagCanvas.planeDistance = 100;
            tagText = UIUtility.CreateText("TagText", tagCanvas.transform, "");
            tagText.gameObject.layer = 5;
            tagText.transform.SetRect(0f,0f,1f,1f,60f, 40f, -60f, -40f);
            tagText.resizeTextForBestFit = false;
            tagText.alignByGeometry = true;
            tagText.fontSize = 192;
            tagText.color = Color.white;
            Outline outline = tagText.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(8, 8);
            Outline outline2 = tagText.gameObject.AddComponent<Outline>();
            outline2.effectColor = Color.black;
            outline2.effectDistance = new Vector2(16, 16);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.MarkLayoutForRebuild(tagText.rectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)tagCanvas.transform);

            tagCanvas.gameObject.SetActive(false);
            tagCanvas.gameObject.SetActive(true);
            //tagCanvas.gameObject.SetActive(false);

            overlayMat = new Material(AssetBundle.LoadFromMemory(Properties.Resources.OverlayShader).LoadAsset<Shader>("OverlayTextures"));
        }

        List<Dropdown.OptionData> GetCategories()
        {
            if(!File.Exists(scenePath)) Directory.CreateDirectory(scenePath);
            var folders = Directory.GetDirectories(scenePath);

            if(folders.Length == 0)
            {
                Directory.CreateDirectory(scenePath + "Category1");
                Directory.CreateDirectory(scenePath + "Category2");
                folders = Directory.GetDirectories(scenePath);
            }

            string[] order;
            if(File.Exists(orderPath))
            {
                order = File.ReadAllLines(orderPath);
            }
            else
            {
                order = new string[0];
                File.Create(orderPath);
            }

            var sorted = folders.Select(x => Path.GetFileName(x)).OrderBy(x => order.Contains(x) ? Array.IndexOf(order, x) : order.Length);
            return sorted.Select(x => new Dropdown.OptionData(x)).ToList();
        }

        private void SetTag(string t)
        {

            tagText.text = t;
            tagInputField.text = t;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.MarkLayoutForRebuild(tagText.rectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)tagCanvas.transform);
            Canvas.ForceUpdateCanvases();
        }

        void LoadScene(string path)
        {
            confirmpanel.gameObject.SetActive(false);
            optionspanel.gameObject.SetActive(false);
            Utils.InvokePluginMethod("LockOnPlugin.LockOnBase", "ResetModState");
            Studio.Studio.Instance.LoadScene(path);
            if(useExternalSavedata) StartCoroutine(StudioNEOExtendSaveMgrLoad(path));
            if(autoClose) UISystem.gameObject.SetActive(false);
        }

        IEnumerator StudioNEOExtendSaveMgrLoad(string path)
        {
            for(int i = 0; i < 3; i++) yield return null;
            Utils.InvokePluginMethod("HSStudioNEOExtSave.StudioNEOExtendSaveMgr", "LoadExtData", path);
            Utils.InvokePluginMethod("HSStudioNEOExtSave.StudioNEOExtendSaveMgr", "LoadExtDataRaw", path);
        }

        void SaveScene()
        {
            UnityEngine.Debug.Log("SaveScene ");
            Studio.Studio.Instance.dicObjectCtrl.Values.ToList().ForEach(x => x.OnSavePreprocessing());
            Studio.Studio.Instance.sceneInfo.cameraSaveData = Studio.Studio.Instance.cameraCtrl.Export();
            string path = GetCategoryFolder() + DateTime.Now.ToString("yyyy_MMdd_HHmm_ss_fff") + ".png";
            tagCanvas.gameObject.SetActive(true);


            
            bslSaving = true;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.MarkLayoutForRebuild(tagText.rectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)tagCanvas.transform);
            Canvas.ForceUpdateCanvases();
            UnityEngine.Debug.Log("CreatePngScreen");
            UnityEngine.Debug.Log("Studio.Studio.Instance.sceneInfo " + Studio.Studio.Instance.sceneInfo.ToString());
            UnityEngine.Debug.Log("Studio.Studio.Instance.sceneInfo.GetType() " + Studio.Studio.Instance.sceneInfo.GetType().ToString());
            UnityEngine.Debug.Log("Studio.Studio.Instance.sceneInfo.GetType().GetField(gameScreenShotAssist, BindingFlags.Instance | BindingFlags.NonPublic) " + Studio.Studio.Instance.sceneInfo.GetType().GetField("gameScreenShotAssist", BindingFlags.Instance | BindingFlags.NonPublic));
            UnityEngine.Debug.Log("Studio.Studio.Instance.sceneInfo " + Studio.Studio.Instance.sceneInfo.ToString());
            UnityEngine.Debug.Log("Studio.Studio.Instance.sceneInfo " + Studio.Studio.Instance.sceneInfo.ToString());

            var privateScreenshotter = Studio.Studio.Instance.sceneInfo.GetType().GetField("gameScreenShotAssist", BindingFlags.Instance | BindingFlags.NonPublic);
            byte[] blah = new byte[10];

            var gssa = privateScreenshotter.GetValue(Studio.Studio.Instance.sceneInfo);
            UnityEngine.Debug.Log("gssa " + gssa.ToString());

            SceneInfoCreatePngScreenPrefix((GameScreenShotAssist)  gssa, ref blah);




            Studio.Studio.Instance.sceneInfo.Save(path);
            bslSaving = false;
            if(useExternalSavedata)
            {
                Utils.InvokePluginMethod("HSStudioNEOExtSave.StudioNEOExtendSaveMgr", "SaveExtData", path);
                //InvokePluginMethod("HSStudioNEOExtSave.StudioNEOExtendSaveMgr", "SaveExtDataRaw", path);
            }

            var button = CreateSceneButton(imagelist.content.GetComponentInChildren<Image>().transform, PngAssist.LoadTexture(path), path);
            button.transform.SetAsFirstSibling();
        }

        private static bool SceneInfoCreatePngScreenPrefix(GameScreenShotAssist ___gameScreenShotAssist, ref byte[] __result)
        {
            if(!bslSaving)
                return true;
            UnityEngine.Debug.Log("SceneInfoCreatePngScreenPrefix because fuck you!");
            tagCamera.transform.position = Camera.main.transform.position;
            tagCamera.transform.rotation = Camera.main.transform.rotation;
            tagCamera.fieldOfView = Camera.main.fieldOfView;
            tagCamera.farClipPlane = Camera.main.farClipPlane;
            tagCamera.nearClipPlane = Camera.main.nearClipPlane;
            tagCamera.rect = new Rect(0, 0, screenshotWidth, screenshotHeight);

            UnityEngine.Debug.Log("tageCamera.pixelWidth " + tagCamera.pixelWidth.ToString());
            UnityEngine.Debug.Log("tagCanvas.pixelRect " + tagCanvas.pixelRect.ToString());
            UnityEngine.Debug.Log("tagCanvas.scaleFactor " + tagCanvas.scaleFactor.ToString());
            UnityEngine.Debug.Log("tagCanvas.GetComponent<CanvasScaler>().scaleFactor " + tagCanvas.GetComponent<CanvasScaler>().scaleFactor.ToString());
            UnityEngine.Debug.Log("tagText.rectTransform " + tagText.rectTransform.anchoredPosition.ToString());
            UnityEngine.Debug.Log("tagText.rectTransform " + tagText.rectTransform.sizeDelta.ToString());
            UnityEngine.Debug.Log("tagText.rectTransform " + tagText.rectTransform.rect.ToString());
            

            int antiAliasing = QualitySettings.antiAliasing != 0 ? QualitySettings.antiAliasing : 1;

            RenderTexture temporary = RenderTexture.GetTemporary(screenshotWidth, screenshotHeight, 24, RenderTextureFormat.Default, RenderTextureReadWrite.Default, antiAliasing);
            tagCamera.targetTexture = temporary;
            tagCamera.Render();
            tagCamera.targetTexture = null;

            overlayMat.SetTexture("_Overlay", temporary);
            overlayMat.SetTextureOffset("_Overlay", Vector2.zero);
            overlayMat.SetTextureScale("_Overlay", Vector2.one);
            overlayMat.SetTexture("_AlphaMask", temporary);

            Graphics.Blit(___gameScreenShotAssist.rtCamera, temporary, overlayMat, 2);

            RenderTexture cachedActive = RenderTexture.active;
            RenderTexture.active = temporary;

            Texture2D texture2D = new Texture2D(screenshotWidth, screenshotHeight, TextureFormat.RGB24, false);
            texture2D.ReadPixels(new Rect(0.0f, 0.0f, screenshotWidth, screenshotHeight), 0, 0);
            texture2D.Apply();

            RenderTexture.active = cachedActive;
            RenderTexture.ReleaseTemporary(temporary);
            __result = texture2D.EncodeToPNG();
            return false;
        }

        void DeleteScene(string path)
        {
            File.Delete(path);
            currentButton.gameObject.SetActive(false);
            confirmpanel.gameObject.SetActive(false);
            optionspanel.gameObject.SetActive(false);
        }

        void ImportScene(string path)
        {
            Studio.Studio.Instance.ImportScene(path);
            confirmpanel.gameObject.SetActive(false);
            optionspanel.gameObject.SetActive(false);
        }

        private void HSExtSaveSceneLoad(string path, XmlNode node)
        {
            this.SetTag(node != null && node.Attributes != null && node.Attributes["tag"] != null ? node.Attributes["tag"].Value.Trim() : "");
        }

        private void HSExtSaveSceneSave(string path, XmlTextWriter writer)
        {
            writer.WriteAttributeString("tag", this.tagInputField.text.Trim());
        }

        void ReloadImages()
        {
            optionspanel.transform.SetParent(imagelist.transform);
            confirmpanel.transform.SetParent(imagelist.transform);
            optionspanel.gameObject.SetActive(false);
            confirmpanel.gameObject.SetActive(false);

            Destroy(imagelist.content.GetComponentInChildren<Image>().gameObject);
            imagelist.content.anchoredPosition = new Vector2(0f, 0f);
            PopulateGrid(true);
        }

        void PopulateGrid(bool forceUpdate = false)
        {
            if(forceUpdate) sceneCache.Remove(category.captionText.text);

            Image sceneList;
            if(sceneCache.TryGetValue(category.captionText.text, out sceneList))
            {
                sceneList.gameObject.SetActive(true);
            }
            else
            {
                List<KeyValuePair<DateTime, string>> scenefiles = (from s in Directory.GetFiles(GetCategoryFolder(), "*.png") select new KeyValuePair<DateTime, string>(File.GetLastWriteTime(s), s)).ToList();
                scenefiles.Sort((KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b) => b.Key.CompareTo(a.Key));

                var container = UIUtility.CreatePanel("GridContainer", imagelist.content.transform);
                container.transform.SetRect(0f, 0f, 1f, 1f);
                container.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var gridlayout = container.gameObject.AddComponent<AutoGridLayout>();
                gridlayout.spacing = new Vector2(marginSize, marginSize);
                gridlayout.m_IsColumn = true;
                gridlayout.m_Column = columnCount;

                StartCoroutine(LoadButtonsAsync(container.transform, scenefiles));
                sceneCache.Add(category.captionText.text, container);
            }
        }

        IEnumerator LoadButtonsAsync(Transform parent, List<KeyValuePair<DateTime, string>> scenefiles)
        {
            string categoryText = category.captionText.text;

            foreach(var scene in scenefiles)
            {
                LoadingIcon.loadingState[categoryText] = true;

                using(WWW www = new WWW("file:///" + scene.Value))
                {
                    yield return www;
                    if(!string.IsNullOrEmpty(www.error)) throw new Exception(www.error);
                    CreateSceneButton(parent, PngAssist.ChangeTextureFromByte(www.bytes), scene.Value);
                }
            }

            LoadingIcon.loadingState[categoryText] = false;
        }

        Button CreateSceneButton(Transform parent, Texture2D texture, string path)
        {
            var button = UIUtility.CreateButton("ImageButton", parent, "");
            button.onClick.AddListener(() =>
            {
                currentButton = button;
                currentPath = path;

                if(optionspanel.transform.parent != button.transform)
                {
                    optionspanel.transform.SetParent(button.transform);
                    optionspanel.transform.SetRect(0f, 0f, 1f, 0.15f);
                    optionspanel.gameObject.SetActive(true);

                    confirmpanel.transform.SetParent(button.transform);
                    confirmpanel.transform.SetRect(0.4f, 0.4f, 0.6f, 0.6f);
                }
                else
                {
                    optionspanel.gameObject.SetActive(!optionspanel.gameObject.activeSelf);
                }

                confirmpanel.gameObject.SetActive(false);
            });

            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            button.gameObject.GetComponent<Image>().sprite = sprite;

            return button;
        }

        string GetCategoryFolder()
        {
            if(category?.captionText?.text != null)
            {
                return scenePath + category.captionText.text + "/";
            }

            return scenePath;
        }
    }
}
