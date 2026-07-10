#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ACT.EditorUI
{

    [UxmlElement]
    public partial class DataSourceBindingPanel : VisualElement
    {
        const string EmptyPreset = "New Preset";
        const string NoneColumn = "None";
        const string DefaultPresetDatabasePath = "Assets/ACT/BindingPresets/DataSourceBindingPresetDatabase.json";

        readonly Color panelColor = new(0.07f, 0.07f, 0.07f);
        readonly Color headerColor = new(0.19f, 0.19f, 0.19f);
        readonly Color sectionColor = new(0.22f, 0.22f, 0.22f);
        readonly Color tableHeaderColor = new(0.15f, 0.15f, 0.15f);
        readonly Color tableColor = new(0.08f, 0.08f, 0.08f);
        readonly Color borderColor = new(0.18f, 0.18f, 0.18f);
        readonly Color strongBorderColor = new(0.23f, 0.23f, 0.23f);
        readonly Color activeColor = new(0.11f, 0.35f, 0.55f);
        readonly Color inactiveColor = new(0.04f, 0.04f, 0.04f);
        readonly Color mappedColor = new(0.13f, 0.19f, 0.13f);
        readonly Color selectedRowColor = new(0.1f, 0.26f, 0.38f);

        readonly List<DataSourcePath> availablePaths = new();
        readonly List<DataSourceValuePath> availableValuePaths = new();
        readonly List<DataSourceValueRecord> valueRecords = new();
        readonly List<DataSourcePathRecord> selectedPaths = new();
        readonly List<DataSourceBindingRow> bindingRows = new();
        readonly Dictionary<string, DataSourceBindingPresetEntry> presetByName = new();
        readonly List<DataSourceTableColumnInfo> tableColumns = new();

        string presetDatabasePath = DefaultPresetDatabasePath;
        string currentPresetName;
        string selectedTargetPath;
        string selectedValuePath;
        string identifierColumn;

        DataSourceBindingMode mode;
        Object keySource;
        TextAsset valueSource;
        DataSourceValueFormat valueFormat;

        ObjectField sourceField;
        TextField dataSourcePathField;
        TextField componentTargetPathField;
        TextField componentTypeField;
        Button addComponentRuleButton;
        VisualElement pathContent;
        Button keyButton;
        Button valueButton;
        DropdownField valueIdentifierColumnField;
        DropdownField presetField;
        Button loadPresetButton;
        Button savePresetButton;
        Button deletePresetButton;
        Button deleteSelectedBindingButton;
        Label bindingTitleLabel;
        VisualElement bindingContent;
        VisualElement bindingTableHeader;

        bool initialized;
        bool dropdownQueued;
        bool suppressDropdownOpen;
        bool suppressColumnChange;
        bool suppressTargetPathChange;

        [UxmlAttribute("preset-database-path")]
        public string PresetDatabasePath
        {
            get => presetDatabasePath;
            set => presetDatabasePath = string.IsNullOrWhiteSpace(value) ? DefaultPresetDatabasePath : value;
        }

        [UxmlAttribute("identifier-column")]
        public string IdentifierColumn
        {
            get => identifierColumn;
            set
            {
                identifierColumn = value ?? "";
                valueIdentifierColumnField?.SetValueWithoutNotify(string.IsNullOrWhiteSpace(identifierColumn) ? NoneColumn : identifierColumn);
                RebuildValuePathsAndRows();
            }
        }

        public IReadOnlyList<DataSourcePathRecord> SelectedPaths => selectedPaths;
        public IReadOnlyList<DataSourceBindingRow> BindingRows => bindingRows;
        public IReadOnlyList<DataSourceValueRecord> ValueRecords => valueRecords;
        public DataSourceValueFormat ValueFormat => valueFormat;

        public event Action<DataSourcePathRecord> PathAdded;
        public event Action<DataSourcePathRecord> PathRemoved;
        public event Action<IReadOnlyList<DataSourcePathRecord>> PathsChanged;
        public event Action<IReadOnlyList<DataSourceBindingRow>> BindingsChanged;

        public DataSourceBindingPanel()
        {
            AddToClassList("act-data-source-binding-panel");
            RegisterCallback<AttachToPanelEvent>(_ => Initialize());
        }

        void Initialize()
        {
            if (initialized) return;

            CacheElements();

            if (!HasRequiredElements())
            {
                BuildDefaultUI();
                CacheElements();
            }

            EnsureComponentRuleRow();
            CacheElements();

            BindUIEvents();
            initialized = true;

            RefreshPresetChoices();
            RebuildBindingHeader();
            SetKeyMode();
        }

        void CacheElements()
        {
            sourceField = this.Q<ObjectField>("DataSourceObjectField");
            dataSourcePathField = this.Q<TextField>("DataSourcePathField");
            componentTargetPathField = this.Q<TextField>("DataSourceComponentTargetPathField");
            componentTypeField = this.Q<TextField>("DataSourceComponentTypeField");
            addComponentRuleButton = this.Q<Button>("DataSourceAddComponentRuleButton");
            pathContent = this.Q<VisualElement>("DataSourcePathContent");
            keyButton = this.Q<Button>("ObjectModeButton");
            valueButton = this.Q<Button>("SOModeButton");
            valueIdentifierColumnField = this.Q<DropdownField>("DataSourceIdentifierColumnField") ?? this.Q<DropdownField>("DataSourceValueKeyColumnField");
            presetField = this.Q<DropdownField>("DataSourcePresetField");
            loadPresetButton = this.Q<Button>("DataSourcePresetLoadButton");
            savePresetButton = this.Q<Button>("DataSourcePresetSaveButton");
            deletePresetButton = this.Q<Button>("DataSourcePresetDeleteButton");
            deleteSelectedBindingButton = this.Q<Button>("DataSourceBindingDeleteSelectedButton");
            bindingTitleLabel = this.Q<Label>("DataSourceBindingTitleLabel");
            bindingContent = this.Q<VisualElement>("DataSourceBindingContent");
            bindingTableHeader = this.Q<VisualElement>("DataSourceBindingTableHeader");
        }

        bool HasRequiredElements()
        {
            return sourceField != null && dataSourcePathField != null && pathContent != null && keyButton != null && valueButton != null &&
                   valueIdentifierColumnField != null && presetField != null && loadPresetButton != null && savePresetButton != null &&
                   deletePresetButton != null && deleteSelectedBindingButton != null && bindingTitleLabel != null && bindingContent != null && bindingTableHeader != null;
        }

        void BindUIEvents()
        {
            dataSourcePathField.isReadOnly = true;
            dataSourcePathField.focusable = true;
            dataSourcePathField.pickingMode = PickingMode.Position;

            dataSourcePathField.RegisterCallback<PointerDownEvent>(OnPathFieldPointerDown, TrickleDown.TrickleDown);
            dataSourcePathField.RegisterCallback<FocusInEvent>(_ => RequestOpenDropdown(), TrickleDown.TrickleDown);
            dataSourcePathField.RegisterCallback<KeyDownEvent>(OnPathFieldKeyDown, TrickleDown.TrickleDown);
            componentTargetPathField?.RegisterValueChangedCallback(OnComponentTargetPathChanged);

            sourceField.RegisterValueChangedCallback(e => OnSourceChanged(e.newValue));
            valueIdentifierColumnField.RegisterValueChangedCallback(e => OnIdentifierColumnChanged(e.newValue));
            if (addComponentRuleButton != null) addComponentRuleButton.clicked += AddManualComponentRule;

            keyButton.clicked += SetKeyMode;
            valueButton.clicked += SetValueMode;
            loadPresetButton.clicked += LoadSelectedPreset;
            savePresetButton.clicked += SaveCurrentPreset;
            deletePresetButton.clicked += DeleteSelectedPreset;
            deleteSelectedBindingButton.clicked += DeleteSelectedBindingRow;

            presetField.RegisterValueChangedCallback(e => currentPresetName = e.newValue == EmptyPreset ? "" : e.newValue);
        }

        void BuildDefaultUI()
        {
            Clear();

            this.SetFlexColumn().SetFlex().SetBackground(panelColor);
            Add(BuildHeader());

            VisualElement content = new VisualElement { name = "BindingContent" }.SetFlexColumn().SetFlex().SetPadding(8);
            Add(content);

            content.Add(BuildModeRow());
            content.Add(BuildSourceRow());
            content.Add(BuildValueColumnRow());
            content.Add(BuildPathRow());
            content.Add(BuildPathList());
            content.Add(BuildComponentRuleRow());
            content.Add(BuildPresetRow());
            content.Add(BuildBindingTitle());
            content.Add(BuildBindingTable());
        }

        VisualElement BuildHeader()
        {
            VisualElement header = VisualElementExtension.CreateRow(24).SetPaddingLeft(6).SetBackground(headerColor);
            header.name = "BindingHeader";
            header.style.flexShrink = 0;
            header.Add(VisualElementExtension.CreateLabel("▼", 16, Color.white, 11, TextAnchor.MiddleCenter));
            header.Add(VisualElementExtension.CreateFlexLabel("Binding"));
            return header;
        }

        VisualElement BuildModeRow()
        {
            VisualElement row = VisualElementExtension.CreateRow(24);
            row.style.flexShrink = 0;

            keyButton = VisualElementExtension.CreateButton("ObjectModeButton", "Key");
            valueButton = VisualElementExtension.CreateButton("SOModeButton", "Value");

            row.Add(VisualElementExtension.CreateLabel("Data Source", 126));
            row.Add(keyButton);
            row.Add(valueButton);
            return row;
        }

        VisualElement BuildSourceRow()
        {
            VisualElement row = VisualElementExtension.CreateRow(24);
            row.style.flexShrink = 0;
            sourceField = VisualElementExtension.CreateObjectField("DataSourceObjectField");
            row.Add(VisualElementExtension.CreateLabel("", 126));
            row.Add(sourceField);
            return row;
        }

        VisualElement BuildValueColumnRow()
        {
            VisualElement row = VisualElementExtension.CreateRow(24).SetMarginTop(4);
            row.name = "DataSourceValueColumnRow";
            row.style.flexShrink = 0;

            valueIdentifierColumnField = VisualElementExtension.CreateDropdownField("DataSourceIdentifierColumnField");
            valueIdentifierColumnField.tooltip = "테이블형 JSON에서 row를 구분할 기준 컬럼입니다. id를 고르면 rows[id=1].HP / 1.HP 형태로 표시됩니다.";

            row.Add(VisualElementExtension.CreateLabel("Row Key", 126));
            row.Add(valueIdentifierColumnField);
            return row;
        }

        VisualElement BuildPathRow()
        {
            VisualElement row = VisualElementExtension.CreateRow(24).SetMarginTop(8);
            row.style.flexShrink = 0;

            dataSourcePathField = VisualElementExtension.CreateTextField("DataSourcePathField");

            row.Add(VisualElementExtension.CreateLabel("Target Property", 126, new Color(0.35f, 0.85f, 1f)));
            row.Add(dataSourcePathField);
            return row;
        }

        VisualElement BuildPathList()
        {
            ScrollView scroll = new ScrollView { name = "DataSourcePathList" };
            scroll.SetHeight(220).SetMarginLeft(126).SetMarginTop(4).SetBackground(new Color(0.11f, 0.11f, 0.11f)).SetBorder(strongBorderColor);
            scroll.style.flexShrink = 0;

            pathContent = new VisualElement { name = "DataSourcePathContent" }.SetFlexColumn().SetFlexGrow();
            scroll.Add(pathContent);
            return scroll;
        }

        VisualElement BuildComponentRuleRow()
        {
            VisualElement box = new VisualElement { name = "DataSourceComponentRuleRow" }.SetFlexColumn().SetMarginTop(6);
            box.style.flexShrink = 0;

            VisualElement pathRow = VisualElementExtension.CreateRow(24);
            componentTargetPathField = VisualElementExtension.CreateTextField("DataSourceComponentTargetPathField");
            componentTargetPathField.tooltip = "비우면 Root. 예: Body, Body/Collider, WeaponSocket";
            pathRow.Add(VisualElementExtension.CreateLabel("Target Path", 126, new Color(0.75f, 0.9f, 1f), 11, TextAnchor.MiddleLeft));
            pathRow.Add(componentTargetPathField);

            VisualElement typeRow = VisualElementExtension.CreateRow(24).SetMarginTop(2);
            componentTypeField = VisualElementExtension.CreateTextField("DataSourceComponentTypeField");
            componentTypeField.tooltip = "컴포넌트 타입명. 예: Animator, CapsuleCollider, UnityEngine.AI.NavMeshAgent";
            addComponentRuleButton = VisualElementExtension.CreateButton("DataSourceAddComponentRuleButton", "+ Component");
            addComponentRuleButton.style.width = 92;
            addComponentRuleButton.style.marginLeft = 4;
            typeRow.Add(VisualElementExtension.CreateLabel("Component", 126, new Color(0.75f, 0.9f, 1f), 11, TextAnchor.MiddleLeft));
            typeRow.Add(componentTypeField);
            typeRow.Add(addComponentRuleButton);

            box.Add(pathRow);
            box.Add(typeRow);
            return box;
        }

        void EnsureComponentRuleRow()
        {
            if (this.Q<VisualElement>("DataSourceComponentRuleRow") != null) return;

            VisualElement content = this.Q<VisualElement>("BindingContent");
            if (content == null) return;

            VisualElement row = BuildComponentRuleRow();
            VisualElement anchor = this.Q<VisualElement>("DataSourcePresetRow") ?? this.Q<VisualElement>("DataSourceBindingTitle");

            if (anchor != null && anchor.parent == content)
                content.Insert(content.IndexOf(anchor), row);
            else
                content.Add(row);
        }

        VisualElement BuildPresetRow()
        {
            VisualElement row = VisualElementExtension.CreateRow(24).SetMarginTop(10);
            row.style.flexShrink = 0;

            presetField = VisualElementExtension.CreateDropdownField("DataSourcePresetField");
            loadPresetButton = VisualElementExtension.CreateSmallButton("DataSourcePresetLoadButton", "Load", 42);
            savePresetButton = VisualElementExtension.CreateSmallButton("DataSourcePresetSaveButton", "Save", 42);
            deletePresetButton = VisualElementExtension.CreateSmallButton("DataSourcePresetDeleteButton", "X", 24);

            row.Add(VisualElementExtension.CreateLabel("Preset", 126));
            row.Add(presetField);
            row.Add(loadPresetButton);
            row.Add(savePresetButton);
            row.Add(deletePresetButton);
            return row;
        }

        VisualElement BuildBindingTitle()
        {
            VisualElement header = VisualElementExtension.CreateRow(24).SetMarginTop(8).SetPaddingLeft(6).SetPaddingRight(4).SetBackground(sectionColor);
            header.name = "DataSourceBindingTitle";
            header.style.flexShrink = 0;

            bindingTitleLabel = VisualElementExtension.CreateFlexLabel(BindingTitleText());
            bindingTitleLabel.name = "DataSourceBindingTitleLabel";

            deleteSelectedBindingButton = VisualElementExtension.CreateSmallButton("DataSourceBindingDeleteSelectedButton", "X", 28);
            deleteSelectedBindingButton.tooltip = "선택된 Row 삭제";
            deleteSelectedBindingButton.style.height = 18;

            header.Add(bindingTitleLabel);
            header.Add(deleteSelectedBindingButton);
            return header;
        }

        VisualElement BuildBindingTable()
        {
            VisualElement table = new VisualElement { name = "DataSourceBindingTable" }.SetFlexColumn().SetFlex().SetMinHeight(140).SetBackground(tableColor).SetBorder(strongBorderColor);

            bindingTableHeader = VisualElementExtension.CreateRow(24).SetBackground(tableHeaderColor).SetBorderBottom(strongBorderColor);
            bindingTableHeader.name = "DataSourceBindingTableHeader";
            bindingTableHeader.style.flexShrink = 0;
            table.Add(bindingTableHeader);

            ScrollView scroll = new ScrollView { name = "DataSourceBindingScroll" }.SetFlex().SetBackground(tableColor) as ScrollView;
            table.Add(scroll);

            bindingContent = new VisualElement { name = "DataSourceBindingContent" }.SetFlexColumn().SetFlexGrow();
            scroll.Add(bindingContent);
            return table;
        }

        void RebuildBindingHeader()
        {
            bindingTableHeader.Clear();

            Label index = VisualElementExtension.CreateHeaderLabel("#", 28);
            Label target = VisualElementExtension.CreateHeaderLabel("Object Target Property");
            Label value = VisualElementExtension.CreateHeaderLabel("Value Target Property", 240);

            target.style.flexGrow = 1;

            bindingTableHeader.Add(index);
            bindingTableHeader.Add(target);
            bindingTableHeader.Add(value);
        }

        void OnSourceChanged(Object newValue)
        {
            if (mode == DataSourceBindingMode.Key)
            {
                keySource = newValue;
                ClearCurrentBindings();
                RefreshPresetChoices();
                Rebuild();
                NotifyAllChanged();
                return;
            }

            valueSource = newValue as TextAsset;
            identifierColumn = "";
            RebuildValueRecords();
            RefreshPresetChoices();
            RefreshBindingTitle();
            Rebuild();
        }

        void OnIdentifierColumnChanged(string value)
        {
            if (suppressColumnChange) return;
            identifierColumn = value == NoneColumn ? "" : value;
            RebuildValuePathsAndRows();
        }

        void RebuildValuePathsAndRows()
        {
            RebuildValueRecords();
            DrawCurrentPathList();
            DrawBindingRows();
        }

        public void SetObjectMode() => SetKeyMode();
        public void SetSOMode() => SetValueMode();

        public void SetKeyMode()
        {
            mode = DataSourceBindingMode.Key;
            SetModeButtonColor(true);
            RefreshSourceField();
            RefreshValueColumnFields();
            Rebuild();
        }

        public void SetValueMode()
        {
            mode = DataSourceBindingMode.Value;
            SetModeButtonColor(false);
            RefreshSourceField();
            RebuildValueRecords();
            RefreshBindingTitle();
            RefreshValueColumnFields();
            Rebuild();
        }

        void RefreshSourceField()
        {
            ForceSetSourceField(mode == DataSourceBindingMode.Key ? keySource : valueSource, mode == DataSourceBindingMode.Key ? typeof(Object) : typeof(TextAsset), mode == DataSourceBindingMode.Key);
        }

        void ForceSetSourceField(Object value, Type type, bool allowSceneObjects)
        {
            sourceField.objectType = type;
            sourceField.allowSceneObjects = allowSceneObjects;
            sourceField.SetValueWithoutNotify(null);
            sourceField.SetValueWithoutNotify(value);
            sourceField.MarkDirtyRepaint();
        }

        void SetModeButtonColor(bool keyMode)
        {
            keyButton.text = "Key";
            valueButton.text = "Value";
            keyButton.style.backgroundColor = keyMode ? activeColor : inactiveColor;
            valueButton.style.backgroundColor = keyMode ? inactiveColor : activeColor;
        }

        string BindingTitleText()
        {
            if (valueSource == null) return "Character Data";
            string assetPath = AssetDatabase.GetAssetPath(valueSource);
            string assetName = string.IsNullOrWhiteSpace(assetPath) ? valueSource.name : Path.GetFileNameWithoutExtension(assetPath);
            return ObjectNames.NicifyVariableName(assetName);
        }

        void RefreshBindingTitle()
        {
            if (bindingTitleLabel != null) bindingTitleLabel.text = BindingTitleText();
            if (deleteSelectedBindingButton != null) deleteSelectedBindingButton.SetEnabled(CurrentBindingRow() != null);
        }

        string ValueRootName()
        {
            if (valueSource == null) return "Value Data";
            string path = AssetDatabase.GetAssetPath(valueSource);
            string name = string.IsNullOrWhiteSpace(path) ? valueSource.name : Path.GetFileNameWithoutExtension(path);
            return ObjectNames.NicifyVariableName(name);
        }

        void RebuildValueRecords()
        {
            valueRecords.Clear();
            availableValuePaths.Clear();
            tableColumns.Clear();
            valueFormat = DataSourceValueFormat.Unknown;

            if (valueSource == null)
            {
                RefreshValueColumnFields();
                return;
            }

            string path = AssetDatabase.GetAssetPath(valueSource);

            if (ValueParser.ParseTextToValueRecords(valueSource.text, path, identifierColumn, out List<DataSourceValueRecord> records, out DataSourceValueFormat format, out List<DataSourceTableColumnInfo> columns))
            {
                valueRecords.AddRange(records);
                tableColumns.AddRange(columns);
                valueFormat = format;
            }

            if (tableColumns.Count > 0)
                identifierColumn = ValueParser.ResolveTableValueKey(tableColumns, identifierColumn);

            foreach (DataSourceValueRecord record in valueRecords)
                AddValuePath(record);

            RefreshValueColumnFields();
        }

        void AddValuePath(DataSourceValueRecord record)
        {
            string path = ValueParser.ValueRecordPath(record);
            if (string.IsNullOrWhiteSpace(path)) return;

            availableValuePaths.Add(new DataSourceValuePath
            {
                Group = record.Group,
                Name = record.Name,
                Path = path,
                MenuPath = ValueParser.ValueRecordMenuPath(record),
                DisplayPath = ValueParser.ValueRecordDisplayPath(record),
                Type = string.IsNullOrWhiteSpace(record.Type) ? "value" : record.Type,
                Record = record
            });
        }

        void RefreshValueColumnFields()
        {
            if (valueIdentifierColumnField == null) return;

            suppressColumnChange = true;

            List<string> choices = new();

            foreach (DataSourceTableColumnInfo column in tableColumns)
                choices.Add(column.Key);

            if (choices.Count == 0) choices.Add(NoneColumn);

            valueIdentifierColumnField.choices = choices;
            valueIdentifierColumnField.SetValueWithoutNotify(string.IsNullOrWhiteSpace(identifierColumn) ? choices[0] : identifierColumn);
            valueIdentifierColumnField.SetEnabled(mode == DataSourceBindingMode.Value && tableColumns.Count > 0);

            suppressColumnChange = false;
        }

        void ClearCurrentBindings()
        {
            selectedTargetPath = "";
            selectedValuePath = "";
            selectedPaths.Clear();
            bindingRows.Clear();
            dataSourcePathField.value = "";
        }

        void NotifyAllChanged()
        {
            PathsChanged?.Invoke(selectedPaths);
        }

        void OnPathFieldPointerDown(PointerDownEvent evt)
        {
            RequestOpenDropdown();
            evt.StopImmediatePropagation();
        }

        void OnPathFieldKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.Space) return;
            RequestOpenDropdown();
            evt.StopImmediatePropagation();
        }

        void RequestOpenDropdown()
        {
            if (dropdownQueued || suppressDropdownOpen) return;

            dropdownQueued = true;
            schedule.Execute(() =>
            {
                dropdownQueued = false;
                if (!suppressDropdownOpen) OpenCurrentPathDropdown();
            }).ExecuteLater(0);
        }

        void SuppressNextOpen()
        {
            suppressDropdownOpen = true;
            dataSourcePathField.Blur();
            schedule.Execute(() => suppressDropdownOpen = false).ExecuteLater(180);
        }

        void OpenCurrentPathDropdown()
        {
            if (mode == DataSourceBindingMode.Key)
            {
                BuildAvailablePaths();
                new DataSourcePathDropdown<DataSourcePath>("Object Target Property", availablePaths, x => x.MenuPath, x => x.Type, SelectObjectPath).Show(dataSourcePathField.worldBound);
                return;
            }

            RebuildValueRecords();
            new DataSourcePathDropdown<DataSourceValuePath>(ValueRootName(), availableValuePaths, x => x.MenuPath, x => x.Type, SelectValuePath).Show(dataSourcePathField.worldBound);
        }

        void SelectObjectPath(DataSourcePath path)
        {
            DataSourcePathRecord record = new()
            {
                Mode = DataSourceBindingMode.Key,
                Source = keySource,
                Target = path.Target,
                Group = path.Group,
                Name = path.Name,
                Path = path.Path,
                Type = path.Type,
                ComponentType = path.ComponentType
            };

            dataSourcePathField.SetValueWithoutNotify(path.Path);
            selectedTargetPath = path.Path;
            SetTargetPathFieldWithoutNotify(ExtractTargetPathFromDisplayPath(path.Path));

            if (!selectedPaths.Exists(x => x.Path == record.Path && x.Type == record.Type))
            {
                selectedPaths.Add(record);
                PathAdded?.Invoke(record);
            }

            if (record.Type != "Component") AddBindingRowFromPath(record);
            SuppressNextOpen();
            DrawCurrentPathList();
            DrawBindingRows();
            PathsChanged?.Invoke(selectedPaths);
        }

        void SelectValuePath(DataSourceValuePath path)
        {
            DataSourceBindingRow row = CurrentBindingRow();

            if (row == null)
            {
                EditorUtility.DisplayDialog("Value Mapping", "먼저 Character Data에서 매핑할 Object Target Row를 선택하세요.", "OK");
                SuppressNextOpen();
                return;
            }

            selectedValuePath = path.Path;
            dataSourcePathField.SetValueWithoutNotify(ValueDisplayText(path.Path));
            row.FieldValue = path.Path;
            BindingsChanged?.Invoke(bindingRows);

            SuppressNextOpen();
            DrawCurrentPathList();
            DrawBindingRows();
        }

        DataSourceBindingRow CurrentBindingRow()
        {
            if (bindingRows.Count == 0) return null;

            DataSourceBindingRow row = bindingRows.Find(x => x.Target != null && x.Target.Path == selectedTargetPath);
            if (row != null) return row;

            selectedTargetPath = bindingRows[0].Target?.Path ?? "";
            selectedValuePath = bindingRows[0].FieldValue ?? "";
            return bindingRows[0];
        }

        int CurrentBindingIndex()
        {
            for (int i = 0; i < bindingRows.Count; i++)
                if (bindingRows[i].Target != null && bindingRows[i].Target.Path == selectedTargetPath)
                    return i;

            return -1;
        }

        void AddBindingRowFromPath(DataSourcePathRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Path)) return;

            DataSourceBindingRow row = bindingRows.Find(x => x.Target != null && x.Target.Path == record.Path);

            if (row == null)
            {
                bindingRows.Add(new DataSourceBindingRow { Target = record.Clone(), FieldValue = "" });
                BindingsChanged?.Invoke(bindingRows);
                return;
            }

            selectedTargetPath = row.Target.Path;
            selectedValuePath = row.FieldValue ?? "";
        }

        void DeleteSelectedBindingRow() => RemoveBindingRow(CurrentBindingIndex());

        void RemoveBindingRow(int index)
        {
            if (index < 0 || index >= bindingRows.Count) return;

            string removedTargetPath = bindingRows[index].Target?.Path ?? "";
            string removedValuePath = bindingRows[index].FieldValue ?? "";

            bindingRows.RemoveAt(index);
            selectedPaths.RemoveAll(x => x.Path == removedTargetPath);

            if (selectedTargetPath == removedTargetPath)
                selectedTargetPath = bindingRows.Count > 0 ? bindingRows[Mathf.Clamp(index, 0, bindingRows.Count - 1)].Target?.Path ?? "" : "";

            if (selectedValuePath == removedValuePath)
                selectedValuePath = "";

            DrawCurrentPathList();
            DrawBindingRows();
            NotifyAllChanged();
        }

        void RemoveSelectedPath(DataSourcePathRecord record)
        {
            if (record == null) return;

            selectedPaths.RemoveAll(x => x.Path == record.Path);
            bindingRows.RemoveAll(x => x.Target != null && x.Target.Path == record.Path);

            if (selectedTargetPath == record.Path)
            {
                selectedTargetPath = bindingRows.Count > 0 ? bindingRows[0].Target?.Path ?? "" : "";
                selectedValuePath = bindingRows.Count > 0 ? bindingRows[0].FieldValue ?? "" : "";
            }

            dataSourcePathField.SetValueWithoutNotify(mode == DataSourceBindingMode.Key ? selectedTargetPath : ValueDisplayText(selectedValuePath));

            PathRemoved?.Invoke(record);
            DrawCurrentPathList();
            DrawBindingRows();
            NotifyAllChanged();
        }

        void Rebuild()
        {
            BuildAvailablePaths();
            RebuildValueRecords();
            DrawCurrentPathList();
            DrawBindingRows();
        }

        void BuildAvailablePaths()
        {
            availablePaths.Clear();

            GameObject go = ResolveGameObject(keySource);
            if (go == null) return;

            Transform root = go.transform;
            Component[] components = go.GetComponentsInChildren<Component>(true);
            Dictionary<string, int> counts = CountComponentTypesByTransform(root, components);
            Dictionary<string, int> indexes = new();

            foreach (Component component in components)
            {
                if (component == null) continue;
                if (component is Transform) continue;

                Type type = component.GetType();
                string targetPath = GetTransformPath(root, component.transform);
                string key = $"{targetPath}|{type.FullName}";

                indexes.TryAdd(key, 0);
                string displayName = ObjectNames.NicifyVariableName(type.Name);
                int count = counts.TryGetValue(key, out int componentCount) ? componentCount : 1;
                AddComponentPaths(root, component, count <= 1 ? displayName : $"{displayName} {++indexes[key]}");
            }
        }

        Dictionary<string, int> CountComponentTypesByTransform(Transform root, Component[] components)
        {
            Dictionary<string, int> counts = new();

            foreach (Component component in components)
            {
                if (component == null || component is Transform) continue;

                string key = $"{GetTransformPath(root, component.transform)}|{component.GetType().FullName}";
                counts.TryAdd(key, 0);
                counts[key]++;
            }

            return counts;
        }

        void AddComponentPaths(Transform root, Component component, string componentName)
        {
            Type type = component.GetType();
            string targetPath = GetTransformPath(root, component.transform);
            string targetLabel = !string.IsNullOrWhiteSpace(targetPath) ? targetPath : "Root";
            string componentPath = BuildTargetPath(targetPath, type.Name);

            availablePaths.Add(new DataSourcePath
            {
                Group = targetLabel,
                Name = type.Name,
                Path = componentPath,
                MenuPath = $"{targetLabel}/{componentName}/+ Component Rule",
                Type = "Component",
                ComponentType = type.AssemblyQualifiedName,
                Target = component
            });

            SerializedObject serializedObject = new(component);
            SerializedProperty property = serializedObject.GetIterator();

            for (bool enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
            {
                if (property.propertyPath == "m_Script") continue;

                availablePaths.Add(new DataSourcePath
                {
                    Group = targetLabel,
                    Name = property.displayName,
                    Path = $"{componentPath}.{property.propertyPath}",
                    MenuPath = $"{targetLabel}/{componentName}/{NicifyPath(property.propertyPath, property.displayName)}",
                    Type = TypeName(property),
                    ComponentType = type.AssemblyQualifiedName,
                    Target = component
                });
            }
        }

        void AddManualComponentRule()
        {
            string targetPath = NormalizeTargetPath((componentTargetPathField?.value ?? string.Empty).Trim().Replace('\\', '/'));
            string componentType = (componentTypeField?.value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(componentType))
            {
                EditorUtility.DisplayDialog("Component Rule", "추가할 Component 타입명을 입력하세요.", "OK");
                return;
            }

            DataSourcePathRecord record = new()
            {
                Mode = DataSourceBindingMode.Key,
                Source = keySource,
                Target = ResolveTransformObject(targetPath),
                Group = !string.IsNullOrWhiteSpace(targetPath) ? targetPath : "Root",
                Name = ShortTypeName(componentType),
                Path = BuildTargetPath(targetPath, ShortTypeName(componentType)),
                Type = "Component",
                ComponentType = componentType
            };

            selectedTargetPath = record.Path;

            if (!selectedPaths.Exists(x => x.Path == record.Path && x.Type == "Component"))
            {
                selectedPaths.Add(record);
                PathAdded?.Invoke(record);
            }

            EnsureComponentBindingRow(record);

            DrawCurrentPathList();
            DrawBindingRows();
            PathsChanged?.Invoke(selectedPaths);
        }

        void EnsureComponentBindingRow(DataSourcePathRecord record)
        {
            if (record == null || record.Type != "Component") return;
            if (bindingRows.Exists(x => x.Target != null && x.Target.Path == record.Path && x.Target.Type == "Component")) return;

            bindingRows.Add(new DataSourceBindingRow { Target = record.Clone(), FieldValue = "" });
        }

        Object ResolveTransformObject(string targetPath)
        {
            GameObject go = ResolveGameObject(keySource);
            if (go == null) return null;

            targetPath = NormalizeTargetPath(targetPath);
            if (string.IsNullOrWhiteSpace(targetPath)) return go.transform;

            Transform found = go.transform.Find(targetPath) ??
                              go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => GetTransformPath(go.transform, x) == targetPath);

            return found;
        }

        string GetTransformPath(Transform root, Transform target)
        {
            if (root == null || target == null || root == target) return string.Empty;

            Stack<string> names = new();

            for (Transform current = target; current != null && current != root; current = current.parent)
                names.Push(current.name);

            return string.Join("/", names);
        }

        string BuildTargetPath(string targetPath, string leaf)
        {
            targetPath = NormalizeTargetPath(targetPath);
            leaf = leaf?.Trim().Trim('/') ?? string.Empty;
            return !string.IsNullOrWhiteSpace(targetPath) ? $"{targetPath.TrimEnd('/')}/{leaf}" : leaf;
        }

        string NormalizeTargetPath(string path)
        {
            path = (path ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(path) || path == "Root") return string.Empty;

            GameObject go = ResolveGameObject(keySource);
            string rootName = go != null ? go.name : string.Empty;

            if (!string.IsNullOrWhiteSpace(rootName) && path == rootName) return string.Empty;
            if (!string.IsNullOrWhiteSpace(rootName) && path.StartsWith(rootName + "/", StringComparison.Ordinal))
                path = path[(rootName.Length + 1)..];

            return path;
        }

        string ShortTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return string.Empty;
            string name = typeName.Split(',')[0].Trim();
            int dot = name.LastIndexOf('.');
            return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
        }

        void SetTargetPathFieldWithoutNotify(string value)
        {
            if (componentTargetPathField == null) return;
            suppressTargetPathChange = true;
            componentTargetPathField.SetValueWithoutNotify(NormalizeTargetPath(value));
            suppressTargetPathChange = false;
        }

        void OnComponentTargetPathChanged(ChangeEvent<string> evt)
        {
            if (suppressTargetPathChange || mode != DataSourceBindingMode.Key) return;
            ApplyTargetPathOverride(evt.newValue);
        }

        void ApplyTargetPathOverride(string nextTargetPath)
        {
            nextTargetPath = NormalizeTargetPath(nextTargetPath);
            if (string.IsNullOrWhiteSpace(selectedTargetPath)) return;

            DataSourcePathRecord selected = selectedPaths.FirstOrDefault(x => x.Path == selectedTargetPath);
            DataSourceBindingRow row = bindingRows.FirstOrDefault(x => x.Target != null && x.Target.Path == selectedTargetPath);
            selected ??= row?.Target;
            if (selected == null) return;

            string oldPath = selected.Path;
            string nextPath = RebuildPathWithTargetPath(selected, nextTargetPath);
            if (string.IsNullOrWhiteSpace(nextPath) || oldPath == nextPath) return;

            RewritePath(selected, nextPath, nextTargetPath);

            if (row?.Target != null && !ReferenceEquals(row.Target, selected))
                RewritePath(row.Target, nextPath, nextTargetPath);

            foreach (DataSourcePathRecord path in selectedPaths.Where(x => x.Path == oldPath).ToList())
                RewritePath(path, nextPath, nextTargetPath);

            foreach (DataSourceBindingRow bindingRow in bindingRows.Where(x => x.Target != null && x.Target.Path == oldPath).ToList())
                RewritePath(bindingRow.Target, nextPath, nextTargetPath);

            selectedTargetPath = nextPath;
            dataSourcePathField.SetValueWithoutNotify(nextPath);
            DrawCurrentPathList();
            DrawBindingRows();
            NotifyAllChanged();
        }

        void RewritePath(DataSourcePathRecord record, string nextPath, string targetPath)
        {
            if (record == null) return;
            record.Path = nextPath;
            record.Group = string.IsNullOrWhiteSpace(targetPath) ? "Root" : targetPath;
            record.Target = ResolveTransformObject(targetPath) ?? record.Target;
        }

        string RebuildPathWithTargetPath(DataSourcePathRecord record, string targetPath)
        {
            if (record == null) return string.Empty;
            string componentName = ComponentNameFromPath(record.Path, record.ComponentType, record.Name);
            string componentPath = BuildTargetPath(targetPath, componentName);
            if (record.Type == "Component") return componentPath;

            string propertyPath = string.Empty;
            if (!string.IsNullOrWhiteSpace(record.Path))
            {
                int dot = record.Path.IndexOf('.');
                if (dot >= 0 && dot < record.Path.Length - 1) propertyPath = record.Path[(dot + 1)..];
            }
            return string.IsNullOrWhiteSpace(propertyPath) ? componentPath : $"{componentPath}.{propertyPath}";
        }

        string ComponentNameFromPath(string path, string componentType, string fallback)
        {
            string beforeProperty = (path ?? string.Empty).Split('.')[0];
            int slash = beforeProperty.LastIndexOf('/');
            string name = slash >= 0 ? beforeProperty[(slash + 1)..] : beforeProperty;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            name = ShortTypeName(componentType);
            return !string.IsNullOrWhiteSpace(name) ? name : fallback;
        }

        void DrawCurrentPathList()
        {
            pathContent.Clear();

            if (mode == DataSourceBindingMode.Key)
            {
                foreach (DataSourcePathRecord path in selectedPaths)
                    pathContent.Add(ObjectPathRow(path));

                return;
            }

            DataSourceValuePath valuePath = CurrentValuePath();
            if (valuePath != null) pathContent.Add(ValuePathRow(valuePath));
        }

        DataSourceValuePath CurrentValuePath()
        {
            string path = selectedValuePath;

            if (string.IsNullOrWhiteSpace(path))
                path = CurrentBindingRow()?.FieldValue ?? "";

            if (string.IsNullOrWhiteSpace(path))
                return null;

            return availableValuePaths.Find(x => x.Path == path) ?? new DataSourceValuePath
            {
                Group = "Fields",
                Name = path,
                Path = path,
                Type = "value",
                MenuPath = $"Fields/{path}",
                DisplayPath = ValueParser.ValuePathDisplayText(path)
            };
        }

        VisualElement ObjectPathRow(DataSourcePathRecord record)
        {
            Color rowColor = record.Path == selectedTargetPath
                ? selectedRowColor
                : bindingRows.Exists(x => x.Target != null && x.Target.Path == record.Path) ? mappedColor : new Color(0.08f, 0.08f, 0.08f);
            VisualElement row = VisualElementExtension.CreateRow(24).SetPaddingLeft(6).SetPaddingRight(4).SetBackground(rowColor).SetBorderBottom(borderColor);

            Label path = VisualElementExtension.CreateFlexLabel(record.Path);
            Label type = VisualElementExtension.CreateLabel(record.Type, 82, new Color(0.7f, 0.7f, 0.7f), 10, TextAnchor.MiddleRight);
            Button remove = VisualElementExtension.CreateSmallButton("", "X", 24);

            remove.style.height = 18;
            remove.style.marginLeft = 4;
            remove.clicked += () => RemoveSelectedPath(record);

            row.Add(path);
            row.Add(type);
            row.Add(remove);

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                selectedTargetPath = record.Path;
                selectedValuePath = bindingRows.Find(x => x.Target != null && x.Target.Path == record.Path)?.FieldValue ?? "";
                dataSourcePathField.SetValueWithoutNotify(record.Path);

                if (record.Type != "Component") AddBindingRowFromPath(record);
                if (record.Type == "Component")
                {
                    EnsureComponentBindingRow(record);
                    SetTargetPathFieldWithoutNotify(ExtractTargetPathFromDisplayPath(record.Path));
                    componentTypeField?.SetValueWithoutNotify(!string.IsNullOrWhiteSpace(record.ComponentType) ? record.ComponentType : record.Name);
                }

                DrawCurrentPathList();
                DrawBindingRows();

                evt.StopPropagation();
            });

            return row;
        }

        VisualElement ValuePathRow(DataSourceValuePath record)
        {
            VisualElement row = VisualElementExtension.CreateRow(24).SetPaddingLeft(6).SetPaddingRight(4).SetBackground(record.Path == selectedValuePath ? selectedRowColor : new Color(0.08f, 0.08f, 0.08f)).SetBorderBottom(borderColor);

            Label path = VisualElementExtension.CreateFlexLabel(ValueDisplayText(record.Path));
            Label type = VisualElementExtension.CreateLabel(record.Type, 82, new Color(0.7f, 0.7f, 0.7f), 10, TextAnchor.MiddleRight);

            path.tooltip = record.Path;

            row.Add(path);
            row.Add(type);

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                selectedValuePath = record.Path;
                dataSourcePathField.SetValueWithoutNotify(ValueDisplayText(record.Path));

                DrawCurrentPathList();
                DrawBindingRows();

                evt.StopPropagation();
            });

            return row;
        }

        void DrawBindingRows()
        {
            RefreshBindingTitle();
            bindingContent.Clear();

            for (int i = 0; i < bindingRows.Count; i++)
                bindingContent.Add(BindingRow(bindingRows[i], i));
        }

        VisualElement BindingRow(DataSourceBindingRow record, int index)
        {
            VisualElement row = VisualElementExtension.CreateRow(24);
            row.style.flexShrink = 0;
            row.SetBackground(record.Target?.Path == selectedTargetPath ? selectedRowColor : index % 2 == 0 ? new Color(0.075f, 0.075f, 0.075f) : new Color(0.095f, 0.095f, 0.095f));
            row.SetBorderBottom(borderColor);

            Label order = VisualElementExtension.CreateLabel((index + 1).ToString(), 28, new Color(0.75f, 0.75f, 0.75f), 10, TextAnchor.MiddleCenter);
            Label target = VisualElementExtension.CreateFlexLabel(record.Target?.Path ?? "");
            Label value = VisualElementExtension.CreateLabel(record.Target?.Type == "Component" ? "Component Rule" : ValueDisplayText(record.FieldValue), 240, record.Target?.Type == "Component" ? new Color(0.75f, 0.8f, 0.9f) : string.IsNullOrWhiteSpace(record.FieldValue) ? new Color(0.55f, 0.55f, 0.55f) : Color.white, 10, TextAnchor.MiddleLeft);

            value.style.paddingLeft = 6;
            value.style.paddingRight = 6;
            value.tooltip = record.Target?.Type == "Component" ? "값 없이 Component만 보장하는 Rule입니다." : string.IsNullOrWhiteSpace(record.FieldValue) ? "Value 모드에서 Target Property를 선택하세요." : record.FieldValue;
            value.pickingMode = PickingMode.Ignore;

            row.Add(order);
            row.Add(target);
            row.Add(value);

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                selectedTargetPath = record.Target?.Path ?? "";
                selectedValuePath = record.FieldValue ?? "";
                dataSourcePathField.SetValueWithoutNotify(mode == DataSourceBindingMode.Key ? selectedTargetPath : ValueDisplayText(selectedValuePath));
                SetTargetPathFieldWithoutNotify(ExtractTargetPathFromDisplayPath(selectedTargetPath));

                DrawCurrentPathList();
                DrawBindingRows();

                evt.StopPropagation();
            });

            return row;
        }

        string ValueDisplayText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Select Value Target Property";

            DataSourceValuePath path = availableValuePaths.Find(x => x.Path == value);

            return path != null
                ? path.DisplayPath
                : ValueParser.ValuePathDisplayText(value);
        }

        void RefreshPresetChoices()
        {
            presetByName.Clear();

            List<string> choices = new() { EmptyPreset };

            if (keySource != null)
            {
                DataSourceBindingPresetDatabase database = LoadDatabase();

                foreach (DataSourceBindingPresetEntry preset in database.Presets)
                {
                    if (!IsPresetForCurrentSource(preset)) continue;

                    string displayName = UniquePresetName(preset.PresetName, choices);

                    choices.Add(displayName);
                    presetByName[displayName] = preset;
                }
            }

            presetField.choices = choices;

            if (string.IsNullOrWhiteSpace(presetField.value) || !choices.Contains(presetField.value))
                presetField.SetValueWithoutNotify(EmptyPreset);

            currentPresetName = presetField.value == EmptyPreset ? "" : presetField.value;
        }

        bool IsPresetForCurrentSource(DataSourceBindingPresetEntry preset)
        {
            if (keySource == null || preset == null) return false;

            string path = AssetPath(keySource);
            string guid = AssetGuid(keySource);

            return (!string.IsNullOrWhiteSpace(guid) && preset.PrefabGuid == guid) ||
                   (!string.IsNullOrWhiteSpace(path) && preset.PrefabPath == path) ||
                   (string.IsNullOrWhiteSpace(preset.PrefabGuid) && string.IsNullOrWhiteSpace(preset.PrefabPath) && preset.PrefabName == keySource.name);
        }

        string UniquePresetName(string name, List<string> choices)
        {
            name = string.IsNullOrWhiteSpace(name) ? "Preset" : name;
            if (!choices.Contains(name)) return name;

            int index = 2;
            string result = $"{name} ({index})";

            while (choices.Contains(result))
                result = $"{name} ({++index})";

            return result;
        }

        void SaveCurrentPreset()
        {
            if (keySource == null)
            {
                EditorUtility.DisplayDialog("Preset", "프리셋을 저장하려면 Data Source에 프리팹을 먼저 선택하세요.", "OK");
                return;
            }

            if (presetByName.TryGetValue(currentPresetName, out DataSourceBindingPresetEntry selected))
            {
                SavePreset(selected.PresetName, true, selected);
                return;
            }

            DataSourcePresetNameWindow.Open("Save Preset", keySource == null ? "Preset" : $"{keySource.name} Preset", presetName => SavePreset(presetName, true));
        }

        void SavePreset(string presetName, bool selectAfterSave, DataSourceBindingPresetEntry selectedPreset = null)
        {
            DataSourceBindingPresetDatabase database = LoadDatabase();
            DataSourceBindingPresetEntry entry = selectedPreset == null ? FindPreset(database, presetName) : database.Presets.Find(x => IsSamePreset(x, selectedPreset));

            if (entry == null)
            {
                entry = new DataSourceBindingPresetEntry();
                database.Presets.Add(entry);
            }

            WriteEntry(entry, presetName);
            SaveDatabase(database);
            RefreshPresetChoices();

            if (selectAfterSave) SelectPresetByEntry(entry);
        }

        DataSourceBindingPresetEntry FindPreset(DataSourceBindingPresetDatabase database, string presetName)
        {
            string path = AssetPath(keySource);
            string guid = AssetGuid(keySource);

            foreach (DataSourceBindingPresetEntry preset in database.Presets)
            {
                if (preset.PresetName != presetName) continue;
                if (!string.IsNullOrWhiteSpace(guid) && preset.PrefabGuid == guid) return preset;
                if (!string.IsNullOrWhiteSpace(path) && preset.PrefabPath == path) return preset;
                if (string.IsNullOrWhiteSpace(preset.PrefabGuid) && string.IsNullOrWhiteSpace(preset.PrefabPath) && preset.PrefabName == keySource?.name) return preset;
            }

            return null;
        }

        void WriteEntry(DataSourceBindingPresetEntry entry, string presetName)
        {
            entry.PresetName = presetName;
            entry.PrefabGuid = AssetGuid(keySource);
            entry.PrefabPath = AssetPath(keySource);
            entry.PrefabName = keySource == null ? "" : keySource.name;
            entry.ValueSourceGuid = AssetGuid(valueSource);
            entry.ValueSourcePath = AssetPath(valueSource);
            entry.ValueSourceName = valueSource == null ? "" : valueSource.name;
            entry.IdentifierColumn = identifierColumn;
            entry.Targets = new List<DataSourceBindingPresetTarget>();

            foreach (DataSourceBindingRow row in bindingRows)
            {
                if (row.Target == null || string.IsNullOrWhiteSpace(row.Target.Path)) continue;

                entry.Targets.Add(new DataSourceBindingPresetTarget
                {
                    Group = row.Target.Group,
                    Name = row.Target.Name,
                    Path = row.Target.Path,
                    Type = row.Target.Type,
                    ComponentType = row.Target.ComponentType,
                    FieldValue = row.FieldValue ?? ""
                });
            }
        }

        void LoadSelectedPreset()
        {
            if (string.IsNullOrWhiteSpace(currentPresetName))
            {
                EditorUtility.DisplayDialog("Preset", "불러올 프리셋을 선택하세요.", "OK");
                return;
            }

            if (!presetByName.TryGetValue(currentPresetName, out DataSourceBindingPresetEntry entry))
            {
                EditorUtility.DisplayDialog("Preset", "프리셋을 찾을 수 없습니다.", "OK");
                RefreshPresetChoices();
                return;
            }

            LoadPreset(entry);
        }

        void LoadPreset(DataSourceBindingPresetEntry entry)
        {
            Object presetSource = LoadPresetPrefabSource(entry);
            if (presetSource != null) keySource = presetSource;

            valueSource = LoadAssetByPathOrGuid<TextAsset>(entry.ValueSourcePath, entry.ValueSourceGuid) ?? valueSource;
            identifierColumn = entry.IdentifierColumn ?? "";
            mode = DataSourceBindingMode.Key;

            SetModeButtonColor(true);
            ForceSetSourceField(keySource, typeof(Object), true);

            BuildAvailablePaths();
            RebuildValueRecords();

            selectedTargetPath = "";
            selectedValuePath = "";
            selectedPaths.Clear();
            bindingRows.Clear();

            foreach (DataSourceBindingPresetTarget target in entry.Targets)
            {
                DataSourcePath matched = availablePaths.Find(x => x.Path == target.Path);

                DataSourcePathRecord record = new()
                {
                    Mode = DataSourceBindingMode.Key,
                    Source = keySource,
                    Target = matched?.Target,
                    Group = target.Group,
                    Name = target.Name,
                    Path = target.Path,
                    Type = target.Type,
                    ComponentType = target.ComponentType
                };

                if (!selectedPaths.Exists(x => x.Path == record.Path)) selectedPaths.Add(record.Clone());
                if (!bindingRows.Exists(x => x.Target != null && x.Target.Path == record.Path))
                    bindingRows.Add(new DataSourceBindingRow { Target = record.Clone(), FieldValue = target.FieldValue ?? "" });
            }

            if (bindingRows.Count > 0)
            {
                selectedTargetPath = bindingRows[0].Target?.Path ?? "";
                selectedValuePath = bindingRows[0].FieldValue ?? "";
                dataSourcePathField.SetValueWithoutNotify(selectedTargetPath);
            }

            RefreshPresetChoices();
            SelectPresetByEntry(entry);
            RefreshBindingTitle();
            RefreshValueColumnFields();
            DrawCurrentPathList();
            DrawBindingRows();
            NotifyAllChanged();
        }

        void DeleteSelectedPreset()
        {
            if (string.IsNullOrWhiteSpace(currentPresetName))
            {
                EditorUtility.DisplayDialog("Preset", "삭제할 프리셋을 선택하세요.", "OK");
                return;
            }

            if (!presetByName.TryGetValue(currentPresetName, out DataSourceBindingPresetEntry selectedPreset))
            {
                EditorUtility.DisplayDialog("Preset", "프리셋을 찾을 수 없습니다.", "OK");
                RefreshPresetChoices();
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete Preset", $"Delete preset?\n{selectedPreset.PresetName}", "Delete", "Cancel")) return;

            DataSourceBindingPresetDatabase database = LoadDatabase();
            database.Presets.RemoveAll(preset => IsSamePreset(preset, selectedPreset));
            SaveDatabase(database);

            currentPresetName = "";
            presetField.SetValueWithoutNotify(EmptyPreset);
            RefreshPresetChoices();
        }

        bool IsSamePreset(DataSourceBindingPresetEntry a, DataSourceBindingPresetEntry b)
        {
            if (a == null || b == null) return false;
            if (!string.IsNullOrWhiteSpace(a.PrefabGuid) && !string.IsNullOrWhiteSpace(b.PrefabGuid)) return a.PresetName == b.PresetName && a.PrefabGuid == b.PrefabGuid;
            if (!string.IsNullOrWhiteSpace(a.PrefabPath) && !string.IsNullOrWhiteSpace(b.PrefabPath)) return a.PresetName == b.PresetName && a.PrefabPath == b.PrefabPath;

            return a.PresetName == b.PresetName &&
                   a.PrefabName == b.PrefabName &&
                   a.ValueSourceGuid == b.ValueSourceGuid &&
                   a.ValueSourcePath == b.ValueSourcePath &&
                   a.ValueSourceName == b.ValueSourceName;
        }

        void SelectPresetByEntry(DataSourceBindingPresetEntry entry)
        {
            foreach (KeyValuePair<string, DataSourceBindingPresetEntry> pair in presetByName)
            {
                if (!IsSamePreset(pair.Value, entry)) continue;

                presetField.SetValueWithoutNotify(pair.Key);
                currentPresetName = pair.Key;
                return;
            }

            presetField.SetValueWithoutNotify(EmptyPreset);
            currentPresetName = "";
        }

        DataSourceBindingPresetDatabase LoadDatabase()
        {
            if (!File.Exists(presetDatabasePath)) return new DataSourceBindingPresetDatabase();

            string json = File.ReadAllText(presetDatabasePath);

            return string.IsNullOrWhiteSpace(json)
                ? new DataSourceBindingPresetDatabase()
                : JsonUtility.FromJson<DataSourceBindingPresetDatabase>(json) ?? new DataSourceBindingPresetDatabase();
        }

        void SaveDatabase(DataSourceBindingPresetDatabase database)
        {
            string directory = Path.GetDirectoryName(presetDatabasePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory.Replace("\\", "/"))) Directory.CreateDirectory(directory.Replace("\\", "/"));
            File.WriteAllText(presetDatabasePath, JsonUtility.ToJson(database, true));
            AssetDatabase.ImportAsset(presetDatabasePath);
            AssetDatabase.Refresh();
        }

        Object LoadPresetPrefabSource(DataSourceBindingPresetEntry entry)
        {
            Object source = LoadAssetByPathOrGuid<Object>(entry.PrefabPath, entry.PrefabGuid);
            if (source != null) return source;

            if (!string.IsNullOrWhiteSpace(entry.PrefabPath))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
                if (prefab != null) return prefab;
            }

            if (!string.IsNullOrWhiteSpace(entry.PrefabGuid))
            {
                string path = AssetDatabase.GUIDToAssetPath(entry.PrefabGuid);
                GameObject prefab = string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) return prefab;
            }

            return null;
        }

        T LoadAssetByPathOrGuid<T>(string path, string guid) where T : Object
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }

            if (string.IsNullOrWhiteSpace(guid)) return null;

            string guidPath = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrWhiteSpace(guidPath) ? null : AssetDatabase.LoadAssetAtPath<T>(guidPath);
        }

        string AssetPath(Object asset)
        {
            if (asset == null) return "";

            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrWhiteSpace(path) ? "" : path;
        }

        string AssetGuid(Object asset)
        {
            string path = AssetPath(asset);
            return string.IsNullOrWhiteSpace(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        }

        GameObject ResolveGameObject(Object value)
        {
            if (value is GameObject go) return go;
            if (value is Component component) return component.gameObject;

            string path = AssetDatabase.GetAssetPath(value);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        string ExtractTargetPathFromDisplayPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            string beforeProperty = path.Split('.')[0];
            int slash = beforeProperty.LastIndexOf('/');
            return slash >= 0 ? beforeProperty[..slash] : string.Empty;
        }

        string NicifyPath(string propertyPath, string displayName)
        {
            string[] parts = propertyPath.Split('.');

            for (int i = 0; i < parts.Length; i++)
                parts[i] = i == parts.Length - 1 ? displayName : ObjectNames.NicifyVariableName(parts[i]);

            return string.Join("/", parts);
        }

        string TypeName(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Integer => "int",
                SerializedPropertyType.Boolean => "bool",
                SerializedPropertyType.Float => "float",
                SerializedPropertyType.String => "string",
                SerializedPropertyType.Color => "Color",
                SerializedPropertyType.ObjectReference => "Object",
                SerializedPropertyType.Enum => "enum",
                SerializedPropertyType.Vector2 => "Vector2",
                SerializedPropertyType.Vector3 => "Vector3",
                SerializedPropertyType.Vector4 => "Vector4",
                SerializedPropertyType.Rect => "Rect",
                SerializedPropertyType.Bounds => "Bounds",
                SerializedPropertyType.Quaternion => "Quaternion",
                SerializedPropertyType.Vector2Int => "Vector2Int",
                SerializedPropertyType.Vector3Int => "Vector3Int",
                SerializedPropertyType.RectInt => "RectInt",
                SerializedPropertyType.BoundsInt => "BoundsInt",
                SerializedPropertyType.ManagedReference => "ManagedReference",
                _ => property.propertyType.ToString()
            };
        }
    }
}
#endif