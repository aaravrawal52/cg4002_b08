using System;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    public sealed partial class ScreenAppMenu
    {
        [Header("Gesture screen options")]
        [SerializeField] RectTransform screenOptions;
        Button navigationFocus;
        Color focusOriginalColor;
        public bool IsSettingsOpen => screenOptions != null && screenOptions.gameObject.activeInHierarchy;
        public Button NavigationFocus => navigationFocus;

        public bool TryToggleSettings(ScreenSurface screen, out string message)
        {
            using var scope = app.UseHand(OwnerHand);
            if (IsSettingsOpen) { Close(); message = "Closed screen options"; return true; }
            message = "Point at an available screen outside an edit or the library";
            if (!app.PointerReady || app.GuideBlocksPointer || app.AdjustModeEnabled || app.PlaceModeEnabled
                || IsModal || IsAdvancingMedia || !CanLaunchOn(screen) || screenOptions == null) return false;
            Close();
            Target = screen;
            launcher.gameObject.SetActive(true);
            screenOptions.gameObject.SetActive(true);
            foreach (var button in screenOptions.GetComponentsInChildren<Button>())
                if (button.name == "Rotate") button.interactable = screen.CanRotate;
            RefreshLauncherControls();
            PositionPanels();
            message = "Screen options / swipe to select; grab to open";
            return true;
        }

        // Persistent prefab callbacks run in the menu owner's hand scope, including fingertip clicks.
        public void RunScreenOption(string command)
        {
            using var scope = app.UseHand(OwnerHand);
            if (command == "app.choose") { ShowPinnedChooser(Target); return; }
            if (command == "menu.back") { Close(); return; }
            app.SendLocal(command);
        }

        public bool TryBrowseMedia(ScreenSurface screen, out string message)
        {
            using var scope = app.UseHand(OwnerHand);
            message = "Photo library unavailable";
            if (IsAdvancingMedia || !CanLaunchOn(screen)) return false;
            Target = screen;
            OpenPhotos();
            message = "Choose a photo or video";
            return true;
        }

        public bool TryBack(out string message)
        {
            if (IsAdvancingMedia) { Close(); message = "Media loading cancelled"; return true; }
            return TryRightClick(out message);
        }

        public bool Navigate(string command, out string message)
        {
            message = "Point at an open menu and wait for tracking";
            if (!app.PointerReady || app.GuideBlocksPointer) return false;
            if (IsModal)
            {
                if (command == "menu.select")
                {
                    if (libraryGrid.FocusedIndex >= 0) return libraryGrid.SelectFocused(out message);
                    if (HoveredButton != null) return TryClick(out message);
                    return libraryGrid.SelectFocused(out message);
                }
                return libraryGrid.Navigate(command, HoveredButton, out message);
            }
            if (!IsChooserOpen && !IsSettingsOpen) return false;
            var choices = Array.FindAll((IsSettingsOpen ? screenOptions : options).GetComponentsInChildren<Button>(),
                b => b != optionTemplate && b.isActiveAndEnabled && b.IsInteractable());
            if (choices.Length == 0) return false;
            if (command == "menu.select")
                return ActivateButton(navigationFocus != null ? navigationFocus : HoveredButton != null ? HoveredButton : choices[0], out message);
            int index = Array.IndexOf(choices, navigationFocus != null ? navigationFocus : HoveredButton);
            int direction = command == "menu.left" || command == "menu.up" ? -1 : 1;
            index = index < 0 ? (direction > 0 ? 0 : choices.Length - 1) : (index + direction + choices.Length) % choices.Length;
            SetNavigationFocus(choices[index]);
            message = "Selected " + choices[index].GetComponentInChildren<Text>().text + " / grab to open";
            return true;
        }

        void SetNavigationFocus(Button next)
        {
            if (navigationFocus != null) navigationFocus.targetGraphic.color = focusOriginalColor;
            navigationFocus = next;
            if (next == null) return;
            focusOriginalColor = next.targetGraphic.color;
            next.targetGraphic.color = new Color(0.2f, 0.75f, 0.55f);
        }
    }
}
