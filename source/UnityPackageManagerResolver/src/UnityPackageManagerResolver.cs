// <copyright file="UnityPackageManagerResolver.cs" company="Google LLC">
// Copyright (C) 2020 Google LLC All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>

using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Google {

[InitializeOnLoad]
public class UnityPackageManagerResolver : AssetPostprocessor {

    /// <summary>
    /// Name of the plugin.
    /// </summary>
    internal const string PLUGIN_NAME = "Unity Package Manager Resolver";

    /// <summary>
    /// The operation to perform when modifying the manifest.
    /// </summary>
    internal enum ManifestModificationMode {
        Add, /// Add package registries that are not in the manifest.
        Remove, /// Remove package registries from the manifest that are in the loaded config.
        Modify, /// Display and add / remove all package registries from the manifest.
    }

    private const string ADD_REGISTRIES_QUESTION =
        "Add the selected Unity Package Manager registries to your project?";
    private const string REMOVE_REGISTRIES_QUESTION =
        "Remove the selected Unity Package Manager registries from your project?";
    private const string ADD_REGISTRIES_DESCRIPTION =
        "Adding a registry will allow you to install, upgrade and remove packages from the " +
        "registry's server in the Unity Package Manager. By adding the selected registries, you " +
        "agree that your use of these registries are subject to their terms of service and you " +
        "acknowledge that data will be collected in accordance with each registry's privacy " +
        "policy.";
    private const string REMOVE_REGISTRIES_DESCRIPTION =
        "Removing a registry will prevent you from installing and upgrading packages from the " +
        "registry's server in the Unity Package Manager. It will not remove packages from the " +
        "registry's server that are already installed";
    private const string ADD_OR_REMOVE_REGISTRIES_QUESTION =
        "Add the selected Unity Package Manager (UPM) registries to and remove the " +
        "unselected UPM registries from your project?";
    private const string MODIFY_MENU_ITEM_DESCRIPTION =
        "You can always add or remove registries at a later time using menu item:\n" +
        "'Assets > External Dependency Manager > Unity Package Manager Resolver > " +
        "Modify Registries'.";

    /// <summary>
    /// Registry scopes provided by the Game Package Registry by Google.
    /// </summary>
    private static readonly string[] googleScopes = new [] { "com.google" };

    /// <summary>
    /// Enables / disables external package registries for Unity Package
    /// Manager.
    /// </summary>
    static UnityPackageManagerResolver() {
        logger.Log("Loaded UnityPackageManagerResolver", level: LogLevel.Verbose);

        RunOnMainThread.Run(() => {
                // Load log preferences.
                VerboseLoggingEnabled = VerboseLoggingEnabled;
                CheckRegistries();
            }, runNow: false);
    }

    /// <summary>
    /// Display documentation.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Unity Package Manager Resolver/Documentation")]
    public static void ShowDocumentation() {
        analytics.OpenUrl(VersionHandlerImpl.DocumentationUrl(
            "#unity-package-manager-resolver-usage"), "Usage");
    }

    /// <summary>
    /// Add the settings dialog for this module to the menu and show the
    /// window when the menu item is selected.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Unity Package Manager Resolver/Settings")]
    public static void ShowSettings() {
         UnityPackageManagerResolverSettingsDialog window =
             (UnityPackageManagerResolverSettingsDialog)EditorWindow.GetWindow(
             typeof(UnityPackageManagerResolverSettingsDialog), true, PluginName + " Settings");
         window.Initialize();
         window.Show();
    }

    /// <summary>
    /// Alias for the settings dialog.
    /// </summary>
    [MenuItem("Window/Google/Game Package Registry/Settings")]
    public static void ShowSettingsAlias() {
        ShowSettings();
    }

    /// <summary>
    /// Check registry status based on current settings.
    /// </summary>
    internal static void CheckRegistries() {
        if (Enable) {
            UpdateManifest(ManifestModificationMode.Add,
                           promptBeforeAction: PromptToAddRegistries,
                           showDisableButton: true);
        }
    }


    /// <summary>
    /// Called by Unity when all assets have been updated and checks to see whether any registries
    /// have changed.
    /// </summary>
    /// <param name="importedAssets">Imported assets.</param>
    /// <param name="deletedAssets">Deleted assets.</param>
    /// <param name="movedAssets">Moved assets.</param>
    /// <param name="movedFromAssetPaths">Moved from asset paths. (unused)</param>
    private static void OnPostprocessAllAssets(string[] importedAssets,
                                               string[] deletedAssets,
                                               string[] movedAssets,
                                               string[] movedFromAssetPaths) {
        if (!Enable) return;
        bool registriesChanged = false;
        var checkAssets = new List<string>(importedAssets);
        checkAssets.AddRange(movedAssets);
        foreach (var asset in checkAssets) {
            if (XmlUnityPackageManagerRegistries.IsRegistriesFile(asset)) {
                registriesChanged = true;
                break;
            }
            AssetImporter importer = AssetImporter.GetAtPath(asset);
            if (importer != null) {
                foreach (var assetLabel in AssetDatabase.GetLabels(importer)) {
                    if (assetLabel == XmlUnityPackageManagerRegistries.REGISTRIES_LABEL) {
                        registriesChanged = true;
                        break;
                    }
                }
            }
        }
        if (registriesChanged) CheckRegistries();
    }

    /// <summary>
    /// Add registries in the XML configuration to the project manifest.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Unity Package Manager Resolver/Add Registries")]
    public static void AddRegistries() {
        UpdateManifest(ManifestModificationMode.Add, promptBeforeAction: true,
                       showDisableButton: false);
    }

    /// <summary>
    /// Remove registries in the XML configuration from the project manifest.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Unity Package Manager Resolver/Remove Registries")]
    public static void RemoveRegistries() {
        UpdateManifest(ManifestModificationMode.Remove, promptBeforeAction: true,
                       showDisableButton: false);
    }

    /// <summary>
    /// Add or remove registries in the project manifest based upon the set available in the XML
    /// configuration.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Unity Package Manager Resolver/Modify Registries")]
    public static void ModifyRegistries() {
        UpdateManifest(ManifestModificationMode.Modify, promptBeforeAction: true,
                       showDisableButton: false);
    }

    /// <summary>
    /// Alias that simplifies adding the Game Package Registry by Google to the project.
    /// manifest.
    /// </summary>
    [MenuItem("Window/Google/Game Package Registry/Add To Project")]
    public static void AddGamePackageManagerRegistries() {
        UpdateManifest(ManifestModificationMode.Add, promptBeforeAction: true,
                       showDisableButton: false, scopePrefixFilter: googleScopes);
    }

    /// <summary>
    /// Alias that simplifies removing the Game Package Registry by Google to the project.
    /// manifest.
    /// </summary>
    [MenuItem("Window/Google/Game Package Registry/Remove From Project")]
    public static void RemoveGamePackageManagerRegistries() {
        UpdateManifest(ManifestModificationMode.Remove, promptBeforeAction: false,
                       showDisableButton: false, scopePrefixFilter: googleScopes);
    }

    /// <summary>
    /// Read registries from XML configuration files.
    /// </summary>
    /// <returns>Dictionary of registries indexed by URL.</returns>
    private static Dictionary<string, UnityPackageManagerRegistry> ReadRegistriesFromXml() {
        // Read registries from XML files.
        var xmlReader = new XmlUnityPackageManagerRegistries();
        xmlReader.ReadAll(logger);
        return xmlReader.Registries;
    }

    /// <summary>
    /// Apply registry changes to the projects manifest.
    /// </summary>
    /// <param name="manifestModifier">Object that modifies the project's manifest.</param>
    /// <param name="availableRegistries">Registries that are available in the
    /// configuration.</param>
    /// <param name="manifestRegistries">Registries that are present in the manifest.</param>
    /// <param name="selectedRegistryUrls">URLs of selected registries, these should be items in
    /// availableRegistries.</param>
    /// <param name="addRegistries">Whether to add selected registries to the manifest.</param>
    /// <param name="removeRegistries">Whether to remove unselected registries from the
    /// manifest.</param>
    /// <param name="invertSelection">If false, adds the selected registries and removes the
    /// unselected registries.  If true, removes the selected registries and adds the unselected
    /// registries.</param>
    /// <returns>true if successful, false otherwise.</returns>
    private static bool SyncRegistriesToManifest(
            PackageManifestModifier manifestModifier,
            Dictionary<string, UnityPackageManagerRegistry> availableRegistries,
            Dictionary<string, List<UnityPackageManagerRegistry>> manifestRegistries,
            HashSet<string> selectedRegistryUrls,
            bool addRegistries = true,
            bool removeRegistries = true,
            bool invertSelection = false) {
        // Build a list of registries to add to and remove from the manifest.
        var registriesToAdd = new List<UnityPackageManagerRegistry>();
        var registriesToRemove = new List<UnityPackageManagerRegistry>();

        foreach (var availableRegistry in availableRegistries.Values) {
            var url = availableRegistry.Url;
            bool isSelected = selectedRegistryUrls.Contains(url);
            if (invertSelection) isSelected = !isSelected;

            bool currentlyInManifest = manifestRegistries.ContainsKey(url);

            if (isSelected) {
                if (addRegistries && !currentlyInManifest) {
                    registriesToAdd.Add(availableRegistry);
                }
            } else {
                if (removeRegistries && currentlyInManifest) {
                    registriesToRemove.Add(availableRegistry);
                }
            }
        }

        bool manifestModified = false;
        if (registriesToAdd.Count > 0) {
            manifestModifier.AddRegistries(registriesToAdd);
            manifestModified = true;
        }
        if (registriesToRemove.Count > 0) {
            manifestModifier.RemoveRegistries(registriesToRemove);
            manifestModified = true;
        }

        bool successful = true;
        if (manifestModified) {
            successful = manifestModifier.WriteManifest();
            if (successful) {
                if (registriesToAdd.Count > 0) {
                    logger.Log(String.Format(
                        "Added registries to {0}:\n{1}",
                        PackageManifestModifier.MANIFEST_FILE_PATH,
                        UnityPackageManagerRegistry.ToString(registriesToAdd)));
                }
                if (registriesToRemove.Count > 0) {
                    logger.Log(String.Format(
                        "Removed registries from {0}:\n{1}",
                        PackageManifestModifier.MANIFEST_FILE_PATH,
                        UnityPackageManagerRegistry.ToString(registriesToRemove)));
                }
                analytics.Report(
                    "registry_manifest/write/success",
                    new KeyValuePair<string, string>[] {
                        new KeyValuePair<string, string>("added", registriesToAdd.Count.ToString()),
                        new KeyValuePair<string, string>("removed",
                                                         registriesToRemove.Count.ToString())
                    },
                    "Project Manifest Modified");
            } else {
                analytics.Report("registry_manifest/write/failed", "Project Manifest Write Failed");
            }
        }
        return successful;
    }

    /// <summary>
    /// Update manifest file based on the settings.
    /// </summary>
    /// <param name="mode">Manifest modification mode being applied.</param>
    /// <param name="promptBeforeAction">Whether to display a window that prompts the user for
    /// confirmation before applying changes.</param>
    /// <param name="showDisableButton">Whether to show a button to disable auto-registry
    /// addition.</param>
    /// <param name="scopePrefixFilter">List of scope prefixes used to filter the set of registries
    /// being operated on.</param>
    private static void UpdateManifest(ManifestModificationMode mode,
                                       bool promptBeforeAction = true,
                                       bool showDisableButton = false,
                                       IEnumerable<string> scopePrefixFilter = null) {
        if (!ScopedRegistriesSupported) {
            logger.Log(String.Format("Scoped registries not supported in this version of Unity."),
                       level: LogLevel.Verbose);
            return;
        }

        PackageManifestModifier modifier = new PackageManifestModifier() { Logger = logger };
        Dictionary<string, List<UnityPackageManagerRegistry>> manifestRegistries =
            modifier.ReadManifest() ? modifier.UnityPackageManagerRegistries : null;
        if (manifestRegistries == null) {
            UnityPackageManagerResolver.analytics.Report(
               "registry_manifest/read/failed",
               "Update Manifest failed: Read/Parse manifest failed");
            return;
        }

        var xmlRegistries = ReadRegistriesFromXml();
        // Filter registries using the scope prefixes.
        if (scopePrefixFilter != null) {
            foreach (var registry in new List<UnityPackageManagerRegistry>(xmlRegistries.Values)) {
                bool removeRegistry = true;
                foreach (var scope in registry.Scopes) {
                    foreach (var scopePrefix in scopePrefixFilter) {
                        if (scope.StartsWith(scopePrefix)) {
                            removeRegistry = false;
                        }
                    }
                }
                if (removeRegistry) xmlRegistries.Remove(registry.Url);
            }
        }

        // Filter the set of considered registries based upon the modification mode.
        HashSet<string> selectedRegistryUrls = null;
        switch (mode) {
            case ManifestModificationMode.Add:
                // Remove all items from the XML loaded registries that are present in the manifest.
                foreach (var url in manifestRegistries.Keys) xmlRegistries.Remove(url);
                selectedRegistryUrls = new HashSet<string>(xmlRegistries.Keys);
                break;
            case ManifestModificationMode.Remove:
                // Remove all items from the XML loaded registries that are not present in the
                // manifest.
                foreach (var url in new List<string>(xmlRegistries.Keys)) {
                    if (!manifestRegistries.ContainsKey(url)) {
                        xmlRegistries.Remove(url);
                    }
                }
                selectedRegistryUrls = new HashSet<string>(xmlRegistries.Keys);
                break;
            case ManifestModificationMode.Modify:
                selectedRegistryUrls = new HashSet<string>();
                // Keep all XML loaded registries and select the items in the manifest.
                foreach (var url in xmlRegistries.Keys) {
                    if (manifestRegistries.ContainsKey(url)) {
                        selectedRegistryUrls.Add(url);
                    }
                }
                break;
        }

        // Applies the manifest modification based upon the modification mode.
        Action<HashSet<string>> syncRegistriesToManifest = (urlSelectionToApply) => {
            SyncRegistriesToManifest(modifier, xmlRegistries, manifestRegistries,
                                     urlSelectionToApply,
                                     addRegistries: (mode == ManifestModificationMode.Add ||
                                                     mode == ManifestModificationMode.Modify),
                                     removeRegistries: (mode == ManifestModificationMode.Remove ||
                                                        mode == ManifestModificationMode.Modify),
                                     invertSelection: mode == ManifestModificationMode.Remove);
        };

        if (xmlRegistries.Count > 0) {
            if (promptBeforeAction) {
                // Build a list of items to display.
                var registryItems = new List<KeyValuePair<string, string>>();
                foreach (var kv in xmlRegistries) {
                    registryItems.Add(new KeyValuePair<string, string>(kv.Key, kv.Value.Name));
                }

                // Optional when prompting is enabled or forced.
                var window = MultiSelectWindow.CreateMultiSelectWindow(PLUGIN_NAME);
                window.minSize = new Vector2(1024, 500);
                window.AvailableItems = registryItems;
                window.Sort(1);
                window.SelectedItems = selectedRegistryUrls;
                switch (mode) {
                    case ManifestModificationMode.Add:
                        window.Caption = String.Format("{0}\n\n{1}\n\n{2}",
                                                       ADD_REGISTRIES_QUESTION,
                                                       ADD_REGISTRIES_DESCRIPTION,
                                                       MODIFY_MENU_ITEM_DESCRIPTION);
                        window.ApplyLabel = "Add Selected Registries";
                        break;
                    case ManifestModificationMode.Remove:
                        window.Caption = String.Format("{0}\n\n{1}{2}",
                                                       REMOVE_REGISTRIES_QUESTION,
                                                       REMOVE_REGISTRIES_DESCRIPTION,
                                                       MODIFY_MENU_ITEM_DESCRIPTION);
                        window.ApplyLabel = "Remove Selected Registries";
                        break;
                    case ManifestModificationMode.Modify:
                        window.Caption = String.Format("{0}\n\n{1} {2}",
                                                       ADD_OR_REMOVE_REGISTRIES_QUESTION,
                                                       ADD_REGISTRIES_DESCRIPTION,
                                                       REMOVE_REGISTRIES_DESCRIPTION);
                        window.ApplyLabel = "Modify Registries";
                        break;
                }
                window.RenderItem = (item) => {
                    var registry = xmlRegistries[item.Key];
                    var termsOfService = registry.TermsOfService;
                    if (!String.IsNullOrEmpty(termsOfService)) {
                        if (GUILayout.Button("View Terms of Service")) {
                            Application.OpenURL(termsOfService);
                        }
                    }
                    var privacyPolicy = registry.PrivacyPolicy;
                    if (!String.IsNullOrEmpty(privacyPolicy)) {
                        if (GUILayout.Button("View Privacy Policy")) {
                            Application.OpenURL(privacyPolicy);
                        }
                    }
                };
                if (showDisableButton) {
                    window.RenderBeforeCancelApply = () => {
                        if (GUILayout.Button("Disable Registry Addition")) {
                            Enable = false;
                            window.Close();
                        }
                    };
                }
                window.OnApply = () => { syncRegistriesToManifest(window.SelectedItems); };
                window.Show();
            } else {
                syncRegistriesToManifest(selectedRegistryUrls);
            }
        }
    }

    /// <summary>
    /// Reset settings of this plugin to default values.
    /// </summary>
    internal static void RestoreDefaultSettings() {
        settings.DeleteKeys(PreferenceKeys);
        analytics.RestoreDefaultSettings();
    }

    // Keys in the editor preferences which control the behavior of this
    // module.
    private const string PreferenceEnable =
        "Google.UnityPackageManagerResolver.Enable";
    private const string PreferencePromptToAddRegistries =
        "Google.UnityPackageManagerResolver.PromptToAddRegistries";
    private const string PreferenceVerboseLoggingEnabled =
        "Google.UnityPackageManagerResolver.VerboseLoggingEnabled";
    // List of preference keys, used to restore default settings.
    private static string[] PreferenceKeys = new[] {
        PreferenceEnable,
        PreferencePromptToAddRegistries,
        PreferenceVerboseLoggingEnabled
    };

    // Name of this plugin.
    private const string PluginName = "Google Unity Package Manager Resolver";

    // Unity started supporting scoped registries from 2018.4.
    internal const float MinimumUnityVersionFloat = 2018.4f;
    internal const string MinimumUnityVersionString = "2018.4";

    // Settings used by this module.
    internal static ProjectSettings settings =
        new ProjectSettings("Google.UnityPackageManagerResolver.");

    /// <summary>
    /// Logger for this module.
    /// </summary>
    private static Logger logger = new Logger();

    // Analytics reporter.
    internal static EditorMeasurement analytics =
        new EditorMeasurement(settings, logger, VersionHandlerImpl.GA_TRACKING_ID,
            "com.google.external-dependency-manager", PLUGIN_NAME, "",
            VersionHandlerImpl.PRIVACY_POLICY) {
        BasePath = "/upmresolver/",
        BaseQuery =
            String.Format("version={0}", UnityPackageManagerResolverVersionNumber.Value.ToString()),
        BaseReportName = "Unity Package Manager Resolver: ",
        InstallSourceFilename = Assembly.GetAssembly(typeof(UnityPackageManagerResolver)).Location
    };

    /// <summary>
    /// Whether to use project level settings.
    /// </summary>
    public static bool UseProjectSettings {
        get { return settings.UseProjectSettings; }
        set { settings.UseProjectSettings = value; }
    }

    /// <summary>
    /// Enable / disable management of external registries.
    /// </summary>
    public static bool Enable {
        get { return settings.GetBool(PreferenceEnable, defaultValue: true); }
        set { settings.SetBool(PreferenceEnable, value); }
    }

    /// <summary>
    /// Enable / disable prompting the user to add registries.
    /// </summary>
    public static bool PromptToAddRegistries {
        get { return settings.GetBool(PreferencePromptToAddRegistries, defaultValue: true); }
        set { settings.SetBool(PreferencePromptToAddRegistries, value); }
    }

    /// <summary>
    /// Enable / disable verbose logging.
    /// </summary>
    public static bool VerboseLoggingEnabled {
        get { return settings.GetBool(PreferenceVerboseLoggingEnabled, defaultValue: false); }
        set {
            settings.SetBool(PreferenceVerboseLoggingEnabled, value);
            logger.Level = ExecutionEnvironment.InBatchMode || value ?
                LogLevel.Verbose : LogLevel.Info;
        }
    }

    /// <summary>
    /// Whether scoped registries are supported in current Unity editor.
    /// </summary>
    public static bool ScopedRegistriesSupported {
        get {
            return VersionHandler.GetUnityVersionMajorMinor() >= MinimumUnityVersionFloat;
        }
    }
}
} // namespace Google
