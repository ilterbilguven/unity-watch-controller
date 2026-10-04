using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AppleWatchController.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WatchUI;

namespace AppleWatchController.Editor
{
    /// <summary>Displays authored Watch UI in a fixed-size UI Toolkit viewport and hosts export layout.</summary>
    /// <remarks>
    /// This Editor-only preview resolves Unity styles; it is not a SwiftUI runtime or Watch device simulator.
    /// Reloading or closing the window cancels any pending layout wait. Export uses a separate temporary window.
    /// </remarks>
    public sealed class WatchUiPreviewWindow : EditorWindow
    {
        /// <summary>The Watch viewport width in UI Toolkit logical points.</summary>
        public const float ScreenWidth = 211f;
        /// <summary>The Watch viewport height in UI Toolkit logical points.</summary>
        public const float ScreenHeight = 257f;

        [SerializeField] private string sourcePath;
        private VisualElement screen;
        private ObjectField sourceField;
        private Label status;
        private Action cancelLayoutWait;
        private bool isExportHost;

        /// <summary>Gets the cloned authored root from the latest successful tree load, or null before one loads.</summary>
        /// <remarks>Await the load task before using resolved styles. This object belongs to the window, not the source asset.</remarks>
        public WatchUiElement WatchRoot { get; private set; }
        /// <summary>Gets the SHA-256 fingerprint of the source dependencies sampled for the current load.</summary>
        /// <remarks>The value can become stale if files change. Export checks it again before accepting output.</remarks>
        public string SourceDependencyFingerprint { get; private set; }

        [MenuItem("Tools/AppleWatch/Preview Selected Watch UI")]
        private static async void PreviewSelected()
        {
            try
            {
                await OpenAsync(AssetDatabase.GetAssetPath(Selection.activeObject));
            }
            catch (Exception exception)
            {
                Debug.LogError("[AppleWatch] Preview failed: " + exception.Message);
            }
        }

        /// <summary>Shows the shared preview window and waits for the source tree's styles and layout.</summary>
        /// <param name="sourcePath">An absolute or project-relative .watch.uxml path inside Assets.</param>
        /// <param name="route">An authored navigation route, or null to show the initial page.</param>
        /// <returns>The visible preview window after its 211 by 257 point layout resolves.</returns>
        /// <remarks>Call on Unity's main thread. Reimports current visual dependencies but does not save changes to the authored tree.</remarks>
        /// <exception cref="ArgumentException">The source is invalid or the requested route does not exist.</exception>
        /// <exception cref="InvalidOperationException">The source cannot load, has no single Watch root, or loses its panel.</exception>
        /// <exception cref="TimeoutException">The panel does not resolve layout within 15 seconds.</exception>
        /// <exception cref="OperationCanceledException">The window closes or a new load replaces the current one.</exception>
        public static async Task<WatchUiPreviewWindow> OpenAsync(string sourcePath, string route = null)
        {
            var window = GetWindow<WatchUiPreviewWindow>();
            window.titleContent = new GUIContent("Watch UI Preview");
            window.minSize = new Vector2(320, 320);
            window.Show();
            await window.LoadSourceAsync(sourcePath, route);
            return window;
        }

        /// <summary>Creates a separate preview window that resolves all authored branches for export.</summary>
        /// <param name="sourcePath">The .watch.uxml asset to import and clone.</param>
        /// <returns>The temporary export host after layout resolves.</returns>
        /// <remarks>
        /// The caller must close the returned window in a finally block. Failed loads close it here.
        /// Export-only layout changes affect the clone, not authored UXML or USS files.
        /// </remarks>
        internal static async Task<WatchUiPreviewWindow> CreateExportHostAsync(string sourcePath)
        {
            var window = CreateInstance<WatchUiPreviewWindow>();
            window.isExportHost = true;
            window.titleContent = new GUIContent("Watch UI Export");
            window.position = new Rect(120, 120, 340, 340);
            window.ShowUtility();
            try
            {
                await window.LoadSourceAsync(sourcePath, null, prepareForExport: true);
                return window;
            }
            catch
            {
                window.Close();
                throw;
            }
        }

        /// <summary>Creates the toolbar, status label, and Watch viewport when Unity initializes the window UI.</summary>
        /// <remarks>Reuses existing window chrome when it is still attached; it does not load a source asset.</remarks>
        public void CreateGUI()
        {
            EnsureChrome();
        }

        private void EnsureChrome()
        {
            if (screen != null && screen.parent != null)
                return;

            rootVisualElement.Clear();
            var chromeStyles = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                WatchPackagePaths.AssetPath("Editor/WatchUI/Resources/WatchUiPreview.uss"))
                ?? Resources.Load<StyleSheet>("WatchUiPreview");
            if (chromeStyles != null && !rootVisualElement.styleSheets.Contains(chromeStyles))
                rootVisualElement.styleSheets.Add(chromeStyles);
            var toolbar = new Toolbar();
            toolbar.AddToClassList("watch-preview-window-toolbar");
            sourceField = new ObjectField { objectType = typeof(VisualTreeAsset), allowSceneObjects = false };
            sourceField.AddToClassList("watch-preview-window-source");
            sourceField.RegisterValueChangedCallback(change => ReloadFromUi(AssetDatabase.GetAssetPath(change.newValue)));
            toolbar.Add(sourceField);
            toolbar.Add(new ToolbarButton(() => ReloadFromUi(sourcePath)) { text = "Reload" });
            toolbar.Add(new ToolbarButton(ExportFromUi) { text = "Export" });
            if (isExportHost)
                toolbar.SetEnabled(false);
            rootVisualElement.Add(toolbar);

            var viewport = new VisualElement();
            viewport.style.flexGrow = 1;
            viewport.style.alignItems = Align.Center;
            viewport.style.justifyContent = Justify.Center;
            screen = new VisualElement { name = "watch-screen" };
            screen.style.width = ScreenWidth;
            screen.style.height = ScreenHeight;
            screen.style.flexShrink = 0;
            screen.style.overflow = Overflow.Hidden;
            viewport.Add(screen);
            rootVisualElement.Add(viewport);
            status = new Label("Select a .watch.uxml asset.");
            status.AddToClassList("watch-preview-window-status");
            rootVisualElement.Add(status);
            SetStatus(status.text);

            if (isExportHost)
            {
                var overlay = new VisualElement { name = "watch-export-overlay" };
                overlay.AddToClassList("watch-preview-window-export-overlay");
                overlay.Add(new Label("Preparing Watch UI export…"));
                rootVisualElement.Add(overlay);
            }
        }

        private void SetStatus(string message)
        {
            status.text = message;
            status.tooltip = message;
        }

        private async void ReloadFromUi(string path)
        {
            try
            {
                await LoadSourceAsync(path, null);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message);
                Debug.LogError("[AppleWatch] Preview failed: " + exception.Message);
            }
        }

        private async void ExportFromUi()
        {
            try
            {
                var result = await WatchUiExportMenu.ExportWatchUiWithExplicitPathAsync(sourcePath);
                if (this != null)
                    SetStatus("Exported " + Path.GetFileName(result.OutputPath));
            }
            catch (Exception exception)
            {
                if (this != null)
                    SetStatus(exception.Message);
                Debug.LogError("[AppleWatch] Export failed: " + exception.Message);
            }
        }

        private async Task LoadSourceAsync(string path, string route, bool prepareForExport = false)
        {
            cancelLayoutWait?.Invoke();
            EnsureChrome();
            var relativePath = WatchUiExportMenu.ResolveProjectRelativeWatchUiPath(path);
            if (string.IsNullOrWhiteSpace(relativePath)
                || !relativePath.EndsWith(".watch.uxml", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(relativePath))
                throw new ArgumentException("Select an existing .watch.uxml asset in this project's Assets folder.");

            ImportCurrentDependencies(relativePath);
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(relativePath);
            if (asset == null)
                throw new InvalidOperationException("Could not load Watch UI source: " + relativePath);

            sourcePath = relativePath;
            sourceField.SetValueWithoutNotify(asset);
            sourceField.tooltip = sourcePath;
            SourceDependencyFingerprint = AppleWatchUiBuildState.ComputeSourceDependenciesSha256(sourcePath);
            screen.Clear();
            screen.styleSheets.Clear();
            asset.CloneTree(screen);
            var roots = screen.Children().ToArray();
            if (roots.Length != 1 || !(roots[0] is WatchUiElement watchRoot))
                throw new InvalidOperationException("Watch UI source must contain exactly one root WatchUiElement.");

            WatchRoot = watchRoot;
            RefreshAuthoredTree(WatchRoot);
            if (prepareForExport)
                PrepareExportLayout(WatchRoot);
            if (!string.IsNullOrWhiteSpace(route))
            {
                var navigation = WatchRoot as WatchNavigationStack ?? WatchRoot.Q<WatchNavigationStack>();
                if (navigation == null || !navigation.NavigateTo(route))
                    throw new ArgumentException("Unknown Watch UI route: " + route);
            }

            SetStatus("Resolving source styles…");
            await WaitForLayoutAsync();
            SetStatus(Path.GetFileName(sourcePath) + " · 211 × 257 pt");
        }

        private static void RefreshAuthoredTree(WatchUiElement element)
        {
            element.RefreshPreview();
            foreach (var child in element.GetAuthoredChildren().OfType<WatchUiElement>())
                RefreshAuthoredTree(child);
        }

        private static void PrepareExportLayout(WatchUiElement element)
        {
            // Hidden branches resolve typography but skip layout-derived values such as padding.
            if (element is WatchNavigationDestination)
                element.style.display = DisplayStyle.Flex;
            if (element is WatchPicker)
                element.contentContainer.style.display = DisplayStyle.Flex;
            foreach (var child in element.GetAuthoredChildren().OfType<WatchUiElement>())
                PrepareExportLayout(child);
        }

        private static void ImportCurrentDependencies(string path)
        {
            const ImportAssetOptions options = ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate;
            AssetDatabase.ImportAsset(path, options);
            var imported = new HashSet<string>(StringComparer.Ordinal) { path };
            while (true)
            {
                var dependencies = AppleWatchUiBuildState.GetSourceDependencyPaths(path)
                    .Where(dependency => !imported.Contains(dependency) && IsVisualDependency(dependency))
                    .OrderBy(dependency => dependency, StringComparer.Ordinal).ToArray();
                if (dependencies.Length == 0)
                    break;
                foreach (var dependency in dependencies)
                {
                    imported.Add(dependency);
                    AssetDatabase.ImportAsset(dependency, options);
                }
            }

            // A reimported USS may now reference a newly added image or nested stylesheet.
            AssetDatabase.ImportAsset(path, options);
        }

        private static bool IsVisualDependency(string path)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != null && (typeof(VisualTreeAsset).IsAssignableFrom(type)
                || typeof(StyleSheet).IsAssignableFrom(type) || typeof(Texture).IsAssignableFrom(type)
                || typeof(Sprite).IsAssignableFrom(type) || typeof(VectorImage).IsAssignableFrom(type)
                || typeof(Font).IsAssignableFrom(type)
                || typeof(UnityEngine.TextCore.Text.FontAsset).IsAssignableFrom(type));
        }

        private Task WaitForLayoutAsync()
        {
            var completion = new TaskCompletionSource<bool>();
            var deadline = EditorApplication.timeSinceStartup + 15;
            var stablePasses = 0;
            IVisualElementScheduledItem scheduled = null;
            EditorApplication.CallbackFunction update = null;
            Action cancel = null;

            void Finish(Exception error)
            {
                scheduled?.Pause();
                EditorApplication.update -= update;
                if (cancelLayoutWait == cancel)
                    cancelLayoutWait = null;
                if (error == null)
                    completion.TrySetResult(true);
                else
                    completion.TrySetException(error);
            }

            cancel = () => Finish(new OperationCanceledException("Watch UI preview was closed or replaced."));
            cancelLayoutWait = cancel;
            update = () =>
            {
                if (this == null || screen.panel == null)
                {
                    Finish(new InvalidOperationException("Watch UI preview panel detached before styles resolved."));
                    return;
                }
                if (EditorApplication.timeSinceStartup >= deadline)
                {
                    Finish(new TimeoutException("Watch UI panel did not resolve styles/layout. Export was not attempted."));
                    return;
                }
                Repaint();
            };
            EditorApplication.update += update;
            scheduled = screen.schedule.Execute(() =>
            {
                var ready = screen.panel != null && WatchRoot?.panel == screen.panel
                    && Mathf.Abs(screen.resolvedStyle.width - ScreenWidth) < 0.1f
                    && Mathf.Abs(screen.resolvedStyle.height - ScreenHeight) < 0.1f
                    && !float.IsNaN(WatchRoot.resolvedStyle.width)
                    && !float.IsNaN(WatchRoot.resolvedStyle.height);
                stablePasses = ready ? stablePasses + 1 : 0;
                if (stablePasses >= 2)
                    Finish(null);
            }).Every(16);
            Repaint();
            return completion.Task;
        }

        private void OnDisable()
        {
            cancelLayoutWait?.Invoke();
        }
    }
}
