﻿/* MIT License

Copyright (c) 2016 Edward Rowe, RedBlueGames

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System.Linq;


namespace RedBlueGames.Tools
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Tool that tries to allow renaming mulitple selections by parsing similar substrings
    /// </summary>
    public class BulkRenamerWindow : EditorWindow
    {
        private const string AssetsMenuPath = "Assets/Rename In Bulk";
        private const string GameObjectMenuPath = "GameObject/Rename In Bulk";

        private Vector2 previewPanelScrollPosition;
        private List<UnityEngine.Object> objectsToRename;
        private List<UnityEngine.Object> objectsToRenameSortedLenghtAndByName;
        private BulkRenamer bulkRenamer;

        [MenuItem(AssetsMenuPath, false, 1011)]
        [MenuItem(GameObjectMenuPath, false, 49)]
        private static void ShowRenameSpritesheetWindow()
        {
            EditorWindow.GetWindow<BulkRenamerWindow>(true, "Bulk Rename", true);
        }

        [MenuItem(AssetsMenuPath, true)]
        [MenuItem(GameObjectMenuPath, true)]
        private static bool IsAssetSelectionValid()
        {
            if (Selection.activeObject == null)
            {
                return false;
            }

            return ObjectIsValidForRename(Selection.activeObject);
        }

        private static bool ObjectIsValidForRename(UnityEngine.Object obj)
        {
            if (AssetDatabase.Contains(obj))
            {
                return true;
            }

            if (obj.GetType() == typeof(GameObject))
            {
                return true;
            }

            return false;
        }

        private void OnEnable()
        {
            Selection.selectionChanged += this.Repaint;

            this.previewPanelScrollPosition = Vector2.zero;
            this.bulkRenamer = new BulkRenamer();

            this.RefreshObjectsToRename();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= this.Repaint;
        }

        private void RefreshObjectsToRename()
        {
            var selectedObjects = Selection.objects;
            this.objectsToRename = new List<UnityEngine.Object>();
            foreach (var selectedObject in selectedObjects)
            {
                if (ObjectIsValidForRename(selectedObject))
                {
                    this.objectsToRename.Add(selectedObject);
                }
            }
        }

        private void OnGUI()
        {
            this.RefreshObjectsToRename();

            EditorGUILayout.HelpBox(
                "BulkRename allows renaming mulitple selections at one time via string replacement and other methods.",
                MessageType.None);

            if (this.objectsToRename.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No objects selected. Select some Assets or scene Objects to rename.",
                    MessageType.Error);
                return;
            }

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Text Replacement", EditorStyles.boldLabel);
            this.bulkRenamer.SearchString = EditorGUILayout.TextField(
                "Search for String",
                this.bulkRenamer.SearchString);
            this.bulkRenamer.ReplacementString = EditorGUILayout.TextField(
                "Replace with",
                this.bulkRenamer.ReplacementString);

            EditorGUILayout.LabelField("Additions", EditorStyles.boldLabel);
            this.bulkRenamer.Prefix = EditorGUILayout.TextField("Prefix", this.bulkRenamer.Prefix);
            this.bulkRenamer.Suffix = EditorGUILayout.TextField("Suffix", this.bulkRenamer.Suffix);

            EditorGUILayout.LabelField("Trimming", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            this.bulkRenamer.NumFrontDeleteChars = EditorGUILayout.IntField(
                "Delete From Front",
                this.bulkRenamer.NumFrontDeleteChars);
            this.bulkRenamer.NumFrontDeleteChars = Mathf.Max(0, this.bulkRenamer.NumFrontDeleteChars);
            this.bulkRenamer.NumBackDeleteChars = EditorGUILayout.IntField(
                "Delete from Back",
                this.bulkRenamer.NumBackDeleteChars);
            this.bulkRenamer.NumBackDeleteChars = Mathf.Max(0, this.bulkRenamer.NumBackDeleteChars);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Enumerating", EditorStyles.boldLabel);
            this.bulkRenamer.CountFormat = EditorGUILayout.TextField(
                "Count Format",
                this.bulkRenamer.CountFormat);

            try
            {
                this.bulkRenamer.StartingCount.ToString(this.bulkRenamer.CountFormat);
            }
            catch (System.FormatException)
            {
                var helpBoxMessage = "Invalid Count Format. Typical formats are D1 for one digit with no " +
                                     "leading zeros, D2, for two, etc." +
                                     "\nSee https://msdn.microsoft.com/en-us/library/dwhawy9k(v=vs.110).aspx" +
                                     " for more formatting options.";
                EditorGUILayout.HelpBox(helpBoxMessage, MessageType.Warning);
            }

            this.bulkRenamer.StartingCount = EditorGUILayout.IntField(
                "Count From",
                this.bulkRenamer.StartingCount);

            if (GUILayout.Button("Rename"))
            {
                this.RenameAssets();
                this.Close();
            }

            EditorGUILayout.Space();
            GUILayout.Box(string.Empty, new GUILayoutOption[] { GUILayout.ExpandWidth(true), GUILayout.Height(1) });

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Diff", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("New Name", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            this.previewPanelScrollPosition = EditorGUILayout.BeginScrollView(this.previewPanelScrollPosition);
            var selectedNames = this.GetNamesFromSelections();
            var namePreviews = this.bulkRenamer.GetRenamedStrings(false, selectedNames);
            var nameDiffs = this.bulkRenamer.GetRenamedStrings(true, selectedNames);
            for (int i = 0; i < namePreviews.Length; ++i)
            {
                EditorGUILayout.BeginHorizontal();

                // Calculate if names differ for use with styles
                var previewObjectname = namePreviews[i];
                var objectName = selectedNames[i];
                bool namesDiffer = previewObjectname != objectName;

                // Display diff
                var diffStyle = namesDiffer ? EditorStyles.boldLabel : new GUIStyle(EditorStyles.label);
                diffStyle.richText = true;
                var diffedName = nameDiffs[i];
                EditorGUILayout.LabelField(diffedName, diffStyle);

                // Display new name
                var style = namesDiffer ? EditorStyles.boldLabel : new GUIStyle(EditorStyles.label);
                EditorGUILayout.LabelField(previewObjectname, style);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private string[] GetNamesFromSelections()
        {
            var names = new string[this.objectsToRename.Count];
            var namesAfterSorting = objectsToRename.OrderBy(x => x.name.Length).ThenBy(x => x.name).ToList();
            for (int i = 0; i < this.objectsToRename.Count; ++i)
            {
                names[i] = namesAfterSorting[i].name;
            }

            return names;
        }

        private void RenameAssets()
        {

            objectsToRenameSortedLenghtAndByName = objectsToRename.OrderBy(x => x.name.Length).ThenBy(x => x.name).ToList();//.ThenBy (x => x.name).

            // Record all the objects to undo stack, though this unfortunately doesn't capture Asset renames
            Undo.RecordObjects(this.objectsToRenameSortedLenghtAndByName.ToArray(), "Bulk Rename");

            var names = this.GetNamesFromSelections();
            var newNames = this.bulkRenamer.GetRenamedStrings(false, names);

            for (int i = 0; i < newNames.Length; ++i)
            {
                var infoString = string.Format(
                                     "Renaming asset {0} of {1}",
                                     i,
                                     newNames.Length);

                EditorUtility.DisplayProgressBar(
                    "Renaming Assets...",
                    infoString,
                    i / (float)newNames.Length);

                this.RenameObject(this.objectsToRenameSortedLenghtAndByName[i], newNames[i]);
            }

            EditorUtility.ClearProgressBar();
        }

        private void RenameObject(UnityEngine.Object obj, string newName)
        {
            if (AssetDatabase.Contains(obj))
            {
                this.RenameAsset(obj, newName);
            }
            else
            {
                this.RenameGameObject(obj, newName);
            }
        }

        private void RenameGameObject(UnityEngine.Object gameObject, string newName)
        {
            gameObject.name = newName;
        }

        private void RenameAsset(UnityEngine.Object asset, string newName)
        {
            var pathToAsset = AssetDatabase.GetAssetPath(asset);
            AssetDatabase.RenameAsset(pathToAsset, newName);
        }
    }
}