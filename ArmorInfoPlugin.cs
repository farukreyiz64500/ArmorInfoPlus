using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using EFT.InventoryLogic;
using EFT;
using JsonType;

namespace ArmorInfo
{
    [BepInPlugin("com.faruk.armorinfo", "Armor Info Mod", "1.0.0")]
    public class ArmorInfoPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource LogSource;
        internal static string LogFilePath;

        private static ArmorInfoPlugin Instance;

        internal static ConfigEntry<KeyboardShortcut> KeybindShowArmorInfo;

        private bool showWindow = false;
        private string itemNameText = string.Empty;

        private string itemIdVal = string.Empty;
        private string bluntVal = string.Empty;
        private string destructVal = string.Empty;

        private Color bluntColor = Color.gray;
        private Color destructColor = Color.gray;
        private static readonly Color NaColor = new Color(0.6f, 0.6f, 0.6f);

        private GUIStyle windowStyle;
        private GUIStyle titleStyle;
        private GUIStyle nameStyle;
        private GUIStyle labelStyle;
        private GUIStyle valueStyle;
        private GUIStyle closeButtonStyle;
        private GUIStyle copyButtonStyle;
        private bool stylesInitialized = false;

        // Texture artık static/cache — her açılışta yeniden üretilmiyor
        private static Texture2D cachedBgTexture;
        private static Texture2D cachedCopyIconTexture;
        private static Texture2D cachedCheckIconTexture;

        private Rect windowRect = new Rect(200, 200, 420, 205);
        private bool centerOnOpen = true;

        // Satır düzeni sabitleri — tüm satırlar bu değerleri paylaşır
        private const float LabelColumnWidth = 135f;
        private const float ColumnGap = 10f;
        private const float RowHeight = 22f;
        private const float RowLeftIndent = 18f;
        private const float CopyButtonSize = 20f;

        // Kopyalandı bildirimini kısa süre göstermek için
        private float copyFeedbackTimer = 0f;
        private const float CopyFeedbackDuration = 1.0f;

        private void Awake()
        {
            Instance = this;
            LogSource = Logger;
            LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArmorInfo_Debug.log");

            try
            {
                File.WriteAllText(LogFilePath, $"--- Armor Info Mod Başlatıldı: {DateTime.Now} ---\n");

                KeybindShowArmorInfo = Config.Bind("General", "Show Armor Info Key", new KeyboardShortcut(KeyCode.F9), "Zırh bilgisi penceresini açmak için kısayol tuşu");

                ArmorContextMenu.InitPatches();
                new ArmorKeybindPatch().Enable();
            }
            catch (Exception ex)
            {
                LogSource?.LogError($"Awake Hatası: {ex}");
            }
        }

        public static void ShowArmorInfo(Item item, IArmorComponentTemplate armorTemplate)
        {
            if (Instance == null || item == null) return;

            Instance.showWindow = true;
            Instance.centerOnOpen = true;
            Instance.copyFeedbackTimer = 0f;

            string itemName = item.LocalizedName();
            if (string.IsNullOrEmpty(itemName))
            {
                itemName = item.TemplateId;
            }

            Instance.itemNameText = itemName;
            Instance.itemIdVal = item.TemplateId;

            if (armorTemplate != null)
            {
                float bluntThroughput = armorTemplate.BluntThroughput;
                float destructibility = 0f;

                var session = ArmorContextMenu.GetSession();
                if (session?.BackEndConfig?.Config?.ArmorMaterials != null)
                {
                    if (session.BackEndConfig.Config.ArmorMaterials.TryGetValue(armorTemplate.ArmorMaterial, out var materialValues))
                    {
                        destructibility = materialValues.Destructibility;
                    }
                }

                Instance.bluntVal = $"{bluntThroughput:F3}";
                Instance.destructVal = $"{destructibility:F2}";
                Instance.bluntColor = Instance.GetBluntColor(bluntThroughput);
                Instance.destructColor = Instance.GetDestructColor(destructibility);

                LogToFile($"Pencere açıldı (armor) -> Ad: {itemName}, Blunt: {bluntThroughput}, Destructibility: {destructibility}");
            }
            else
            {
                // Armor olmayan item: stat alanları N/A, nötr renk
                Instance.bluntVal = "N/A";
                Instance.destructVal = "N/A";
                Instance.bluntColor = NaColor;
                Instance.destructColor = NaColor;

                LogToFile($"Pencere açıldı (armor değil) -> Ad: {itemName}, TemplateId: {item.TemplateId}");
            }

            // Not: stylesInitialized artık burada resetlenmiyor — texture/stil zaten cache'li,
            // her açılışta yeniden üretmeye gerek yok (performans).
        }

        private static Texture2D CreateCopyIconTexture()
        {
            int size = 16;
            var tex = new Texture2D(size, size);
            var clear = new Color(0, 0, 0, 0);
            var line = new Color(0.85f, 0.85f, 0.85f, 1f);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, clear);

            // Arka kare (5,2) - (13,10) dış hat
            DrawRectOutline(tex, 5, 5, 13, 13, line);
            // Ön kare (2,2) - (10,10) dış hat, öndeki belge
            DrawRectOutline(tex, 2, 2, 10, 10, line);

            tex.Apply();
            return tex;
        }

        private static Texture2D CreateCheckIconTexture()
        {
            int size = 16;
            var tex = new Texture2D(size, size);
            var clear = new Color(0, 0, 0, 0);
            var line = new Color(0.92f, 0.92f, 0.92f, 1f); // Kırık beyaz, çok parlak değil

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, clear);

            // Koordinatlar "ekran" uzayında (y aşağı doğru artar) tanımlanıp
            // texture'a yazılırken dikey çevriliyor (Texture2D orijini sol-alt).
            // Kısa kol: sol uçtan orta-alt köşeye
            DrawLineScreenSpace(tex, size, 3, 8, 6, 11, line);
            // Uzun kol: orta-alt köşeden sağ üst uca
            DrawLineScreenSpace(tex, size, 6, 11, 13, 3, line);

            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private static void DrawLineScreenSpace(Texture2D tex, int size, int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            // Çizginin baskın yönüne göre, karşı eksende %50 opaklıklı bir komşu piksel
            // ekleyerek ~1.5px kalınlığında yumuşak (anti-alias hissi veren) bir çizgi simüle ediyoruz.
            bool moreHorizontal = dx > -dy;
            var softColor = new Color(color.r, color.g, color.b, color.a * 0.5f);

            int cx = x0, cy = y0;
            while (true)
            {
                int texY = (size - 1) - cy; // ekran-y -> texture-y çevrimi
                SetThickPixel(tex, cx, texY, color);

                if (moreHorizontal)
                    SetThickPixel(tex, cx, texY + 1, softColor);
                else
                    SetThickPixel(tex, cx + 1, texY, softColor);

                if (cx == x1 && cy == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; cx += sx; }
                if (e2 <= dx) { err += dx; cy += sy; }
            }
        }

        private static void DrawRectOutline(Texture2D tex, int x0, int y0, int x1, int y1, Color color)
        {
            for (int x = x0; x <= x1; x++)
            {
                SetThickPixel(tex, x, y0, color);
                SetThickPixel(tex, x, y1, color);
            }
            for (int y = y0; y <= y1; y++)
            {
                SetThickPixel(tex, x0, y, color);
                SetThickPixel(tex, x1, y, color);
            }
        }

        private static void SetThickPixel(Texture2D tex, int x, int y, Color color)
        {
            if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
                tex.SetPixel(x, y, color);
        }

        // Yeşil -> Turuncu -> Kırmızı arasında YUMUŞAK geçiş yapan gradyan.
        // greenAt altı tam yeşil, orangeAt'te tam turuncu, redAt üstü tam kırmızı;
        // aradaki her değer iki komşu renk arasında oranlı olarak karışır.
        private static Color GetGradientColor(float val, float greenAt, float orangeAt, float redAt)
        {
            Color green = new Color(0.25f, 0.75f, 0.4f);
            Color orange = new Color(1.0f, 0.60f, 0.2f);
            Color red = new Color(0.85f, 0.25f, 0.25f);

            if (val <= greenAt) return green;
            if (val >= redAt) return red;

            if (val <= orangeAt)
            {
                float t = Mathf.InverseLerp(greenAt, orangeAt, val);
                return Color.Lerp(green, orange, t);
            }
            else
            {
                float t = Mathf.InverseLerp(orangeAt, redAt, val);
                return Color.Lerp(orange, red, t);
            }
        }

        // Blunt Throughput: gerçek item veritabanı taramasına göre ayarlı kontrol noktaları
        // (gerçekçi aralık ~0.04 - ~0.40; 0.28 civarı orta-üst dilimin başlangıcı)
        private Color GetBluntColor(float val)
        {
            return GetGradientColor(val, greenAt: 0.10f, orangeAt: 0.28f, redAt: 0.40f);
        }

        // Destructibility: globals.json -> config.ArmorMaterials tablosundan gerçek verilerle kalibre edildi
        // (Aramid 0.1875 ... Ceramic/Glass 0.6 arası gerçek materyal aralığı)
        private Color GetDestructColor(float val)
        {
            return GetGradientColor(val, greenAt: 0.20f, orangeAt: 0.40f, redAt: 0.60f);
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            if (cachedBgTexture == null)
            {
                int w = 420;
                int h = 205;
                cachedBgTexture = new Texture2D(w, h);

                Color fillColor = new Color(0.08f, 0.12f, 0.22f, 1.0f);
                Color borderColor = new Color(0.65f, 0.65f, 0.65f, 1.0f);
                Color bevelLight = new Color(0.85f, 0.85f, 0.85f, 1.0f);
                Color bevelDark = new Color(0.40f, 0.40f, 0.40f, 1.0f);

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (x == 0 || y == 0)
                            cachedBgTexture.SetPixel(x, y, bevelLight);
                        else if (x == w - 1 || y == h - 1)
                            cachedBgTexture.SetPixel(x, y, bevelDark);
                        else if (x == 1 || y == 1 || x == w - 2 || y == h - 2)
                            cachedBgTexture.SetPixel(x, y, borderColor);
                        else
                            cachedBgTexture.SetPixel(x, y, fillColor);
                    }
                }
                cachedBgTexture.Apply();
            }

            if (cachedCopyIconTexture == null)
            {
                cachedCopyIconTexture = CreateCopyIconTexture();
            }

            if (cachedCheckIconTexture == null)
            {
                cachedCheckIconTexture = CreateCheckIconTexture();
            }

            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = cachedBgTexture;
            windowStyle.onNormal.background = cachedBgTexture;
            windowStyle.normal.textColor = Color.white;
            windowStyle.border = new RectOffset(4, 4, 4, 4);

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 16;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.normal.textColor = new Color(0.9f, 0.95f, 1.0f);

            nameStyle = new GUIStyle(GUI.skin.label);
            nameStyle.fontSize = 13;
            nameStyle.fontStyle = FontStyle.Bold;
            nameStyle.alignment = TextAnchor.MiddleCenter;
            nameStyle.normal.textColor = new Color(0.7f, 0.75f, 0.85f);

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.alignment = TextAnchor.MiddleLeft;
            labelStyle.normal.textColor = new Color(0.705f, 0.713f, 0.721f, 1.0f);

            valueStyle = new GUIStyle(GUI.skin.label);
            valueStyle.fontSize = 14;
            valueStyle.fontStyle = FontStyle.Bold;
            valueStyle.alignment = TextAnchor.MiddleLeft;

            closeButtonStyle = new GUIStyle(GUI.skin.button);
            closeButtonStyle.fontSize = 12;
            closeButtonStyle.fontStyle = FontStyle.Bold;
            closeButtonStyle.normal.textColor = Color.white;

            copyButtonStyle = new GUIStyle(GUI.skin.button);
            copyButtonStyle.fontSize = 11;
            copyButtonStyle.fontStyle = FontStyle.Bold;
            copyButtonStyle.alignment = TextAnchor.MiddleCenter;
            copyButtonStyle.padding = new RectOffset(0, 0, 0, 0);
            copyButtonStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (!showWindow) return;

            InitStyles();

            if (copyFeedbackTimer > 0f)
            {
                copyFeedbackTimer -= Time.unscaledDeltaTime;
            }

            if (centerOnOpen)
            {
                windowRect.x = (Screen.width - windowRect.width) / 2f;
                windowRect.y = (Screen.height - windowRect.height) / 2f;
                centerOnOpen = false;
            }

            windowRect = GUI.ModalWindow(1999, windowRect, DrawWindowContent, "", windowStyle);
        }

        // Tek bir satır çizer: label ve value AYNI sabit yükseklikteki Rect içinde,
        // aynı hizalama stiliyle render edilir. Value'nun uzunluğu (ör. 24 haneli ID)
        // başlangıç x konumunu ETKİLEMEZ çünkü label sütunu sabit genişlikte (Width).
        private void DrawRow(string label, string value, Color valueColor, float labelWidthOverride = -1f, float gapOverride = -1f, bool showCopyButton = false)
        {
            float labelWidth = labelWidthOverride >= 0f ? labelWidthOverride : LabelColumnWidth;
            float gap = gapOverride >= 0f ? gapOverride : ColumnGap;

            GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));
            GUILayout.Space(RowLeftIndent);

            GUILayout.Label(label, labelStyle, GUILayout.Width(labelWidth), GUILayout.Height(RowHeight));
            GUILayout.Space(gap);

            var prevColor = valueStyle.normal.textColor;
            valueStyle.normal.textColor = valueColor;
            GUILayout.Label(value, valueStyle, GUILayout.Height(RowHeight), GUILayout.ExpandWidth(false));
            valueStyle.normal.textColor = prevColor;

            if (showCopyButton)
            {
                GUILayout.Space(5.5f);

                bool showingOk = copyFeedbackTimer > 0f;
                Texture2D iconTex = showingOk ? cachedCheckIconTexture : cachedCopyIconTexture;
                if (GUILayout.Button(new GUIContent(iconTex), copyButtonStyle, GUILayout.Width(CopyButtonSize), GUILayout.Height(CopyButtonSize)))
                {
                    GUIUtility.systemCopyBuffer = value;
                    copyFeedbackTimer = CopyFeedbackDuration;
                    LogToFile($"Item ID panoya kopyalandı: {value}");
                }

                GUILayout.Space(4);
            }

            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();
        }

        private void DrawWindowContent(int windowID)
        {
            GUILayout.Space(4);

            GUILayout.Label("ITEM INFO", titleStyle);
            GUILayout.Space(2);
            GUILayout.Label(itemNameText, nameStyle);

            GUILayout.Space(12);

            Color goldColor = ColorUtility.TryParseHtmlString("#C2A64E", out var parsedGold) ? parsedGold : Color.yellow;

            // Item ID satırı: label sütunu daraltılarak value label'a daha yakın başlıyor
            // (bu satır diğer ikisinden bağımsız, kendi labelWidth'ine sahip), ve kopyala butonu içeriyor
            DrawRow("Item ID:", itemIdVal, goldColor, labelWidthOverride: 70f, showCopyButton: true);
            GUILayout.Space(4);

            DrawRow("Blunt Throughput:", bluntVal, bluntColor);
            GUILayout.Space(4);

            DrawRow("Destructibility:", destructVal, destructColor);

            if (GUI.Button(new Rect(windowRect.width - 35, 4, 30, 24), "X", closeButtonStyle))
            {
                showWindow = false;
            }

            GUI.DragWindow();
        }

        internal static void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(LogFilePath, message + "\n");
                LogSource?.LogInfo(message);
            }
            catch { }
        }
    }
}