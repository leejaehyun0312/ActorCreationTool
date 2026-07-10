#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace ACT.EditorUI
{
    public enum DataSourceValueFormat
    {
        Unknown,
        Json,
        Xml
    }

    [Serializable]
    public class DataSourceValueRecord
    {
        public string Name;
        public string Path;
        public string Type;
        public string Value;
        public string Group;
        public string MenuPath;
        public string DisplayPath;
        public DataSourceValueFormat Format;
    }

    [Serializable]
    public class DataSourceTableColumnInfo
    {
        public string Key;
        public string Type;
    }

    public static class ValueParser
    {
        public static bool ParseStringToInt(string text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        public static bool ParseStringToFloat(string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        public static bool ParseStringToBool(string text, out bool value) => bool.TryParse(text, out value);

        public static bool ParseStringToVector2(string text, out Vector2 value)
        {
            value = default;
            if (!ParseStringToFloatArray(text, 2, out float[] values)) return false;
            value = new Vector2(values[0], values[1]);
            return true;
        }

        public static bool ParseStringToVector3(string text, out Vector3 value)
        {
            value = default;
            if (!ParseStringToFloatArray(text, 3, out float[] values)) return false;
            value = new Vector3(values[0], values[1], values[2]);
            return true;
        }

        public static bool ParseStringToVector4(string text, out Vector4 value)
        {
            value = default;
            if (!ParseStringToFloatArray(text, 4, out float[] values)) return false;
            value = new Vector4(values[0], values[1], values[2], values[3]);
            return true;
        }

        public static bool ParseStringToColor(string text, out Color value)
        {
            value = default;
            if (ColorUtility.TryParseHtmlString(text, out value)) return true;
            if (!ParseStringToFloatArray(text, 4, out float[] values)) return false;
            value = new Color(values[0], values[1], values[2], values[3]);
            return true;
        }

        public static bool ParseStringToQuaternion(string text, out Quaternion value)
        {
            value = default;

            if (ParseStringToFloatArray(text, 4, out float[] q))
            {
                value = new Quaternion(q[0], q[1], q[2], q[3]);
                return true;
            }

            if (ParseStringToFloatArray(text, 3, out float[] e))
            {
                value = Quaternion.Euler(e[0], e[1], e[2]);
                return true;
            }

            return false;
        }

        public static bool ParseStringToFloatArray(string text, int count, out float[] values)
        {
            values = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string cleaned = text.Replace("(", "").Replace(")", "").Replace("[", "").Replace("]", "").Replace("{", "").Replace("}", "");
            string[] parts = cleaned.Split(',');

            if (parts.Length != count) return false;

            values = new float[count];

            for (int i = 0; i < count; i++)
                if (!ParseStringToFloat(parts[i].Trim(), out values[i]))
                    return false;

            return true;
        }

        public static bool ParseTextToValueRecords(string text, string assetPath, out List<DataSourceValueRecord> records, out DataSourceValueFormat format)
        {
            return ParseTextToValueRecords(text, assetPath, "", out records, out format, out _);
        }

        public static bool ParseTextToValueRecords(string text, string assetPath, string valueKeyColumn, out List<DataSourceValueRecord> records, out DataSourceValueFormat format, out List<DataSourceTableColumnInfo> tableColumns)
        {
            records = new List<DataSourceValueRecord>();
            tableColumns = new List<DataSourceTableColumnInfo>();
            format = DataSourceValueFormat.Unknown;

            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            string extension = System.IO.Path.GetExtension(assetPath ?? "").ToLowerInvariant();

            try
            {
                if (extension == ".xml" || trimmed.StartsWith("<"))
                {
                    records = ParseXmlToValueRecords(trimmed);
                    format = DataSourceValueFormat.Xml;
                    return records.Count > 0;
                }

                if (TryParseTableJsonToValueRecords(trimmed, valueKeyColumn, out records, out tableColumns))
                {
                    format = DataSourceValueFormat.Json;
                    return records.Count > 0;
                }

                records = ParseJsonToValueRecords(trimmed);
                format = DataSourceValueFormat.Json;
                return records.Count > 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ValueParser] Value parse failed.\n{e.Message}");
                records.Clear();
                tableColumns.Clear();
                format = DataSourceValueFormat.Unknown;
                return false;
            }
        }

        public static bool TryParseTableJsonToValueRecords(string text, string valueKeyColumn, out List<DataSourceValueRecord> records, out List<DataSourceTableColumnInfo> columns)
        {
            records = new List<DataSourceValueRecord>();
            columns = new List<DataSourceTableColumnInfo>();

            object rootObject = ParseJsonToObject(text);
            if (rootObject is not Dictionary<string, object> root) return false;
            if (!root.TryGetValue("columns", out object columnsObject) || columnsObject is not List<object> rawColumns) return false;
            if (!root.TryGetValue("rows", out object rowsObject) || rowsObject is not List<object> rawRows) return false;

            foreach (object columnObject in rawColumns)
            {
                if (columnObject is not Dictionary<string, object> column) continue;

                string key = ReadDictionaryString(column, "key");
                if (string.IsNullOrWhiteSpace(key)) continue;

                columns.Add(new DataSourceTableColumnInfo
                {
                    Key = key,
                    Type = ReadDictionaryString(column, "type")
                });
            }

            if (columns.Count == 0 || rawRows.Count == 0) return false;

            string keyColumn = ResolveTableValueKey(columns, valueKeyColumn);

            foreach (object rowObject in rawRows)
            {
                if (rowObject is not Dictionary<string, object> row) continue;

                string keyValue = ReadDictionaryString(row, keyColumn);
                string rowLabel = string.IsNullOrWhiteSpace(keyValue) ? "Row" : keyValue;

                foreach (DataSourceTableColumnInfo column in columns)
                {
                    if (string.IsNullOrWhiteSpace(column.Key)) continue;

                    string value = ReadDictionaryString(row, column.Key);
                    string path = $"rows[{EscapePathToken(keyColumn)}={EscapePathToken(keyValue)}].{column.Key}";

                    records.Add(new DataSourceValueRecord
                    {
                        Name = column.Key,
                        Path = path,
                        Type = string.IsNullOrWhiteSpace(column.Type) ? GuessStringValueType(value) : column.Type,
                        Value = value,
                        Group = $"Rows/{rowLabel}",
                        MenuPath = $"Rows/{rowLabel}/{column.Key}",
                        DisplayPath = $"{rowLabel}.{column.Key}",
                        Format = DataSourceValueFormat.Json
                    });
                }
            }

            return records.Count > 0;
        }

        public static string ResolveTableValueKey(IReadOnlyList<DataSourceTableColumnInfo> columns, string valueKeyColumn)
        {
            if (columns == null || columns.Count == 0) return "";
            if (!string.IsNullOrWhiteSpace(valueKeyColumn) && columns.Any(x => x.Key == valueKeyColumn)) return valueKeyColumn;

            DataSourceTableColumnInfo id = columns.FirstOrDefault(x => string.Equals(x.Key, "id", StringComparison.OrdinalIgnoreCase));
            if (id != null) return id.Key;

            DataSourceTableColumnInfo name = columns.FirstOrDefault(x => string.Equals(x.Key, "name", StringComparison.OrdinalIgnoreCase));
            return name != null ? name.Key : columns[0].Key;
        }

        public static string EscapePathToken(string value) => (value ?? "").Replace("\\", "\\\\").Replace("=", "\\=").Replace("]", "\\]");

        public static string UnescapePathToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            StringBuilder builder = new();

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    builder.Append(value[++i]);
                    continue;
                }

                builder.Append(value[i]);
            }

            return builder.ToString();
        }

        public static string ValueRecordPath(DataSourceValueRecord record)
        {
            if (record == null) return "";
            return !string.IsNullOrWhiteSpace(record.Path) ? record.Path : record.Name ?? "";
        }

        public static string ValueRecordMenuPath(DataSourceValueRecord record)
        {
            if (record == null) return "";
            if (!string.IsNullOrWhiteSpace(record.MenuPath)) return record.MenuPath;

            string path = ValueRecordPath(record);
            string group = ValueRecordGroup(path, record);
            string name = ValueRecordName(path, record);

            return $"{group}/{name}";
        }

        public static string ValueRecordDisplayPath(DataSourceValueRecord record)
        {
            if (record == null) return "";
            if (!string.IsNullOrWhiteSpace(record.DisplayPath)) return record.DisplayPath;
            return ValuePathDisplayText(ValueRecordPath(record), ValueRecordMenuPath(record));
        }

        public static string ValuePathDisplayText(string path, string menuPath = "")
        {
            if (string.IsNullOrWhiteSpace(path)) return "";

            if (TryFormatTablePath(path, out string display))
                return display;

            return string.IsNullOrWhiteSpace(menuPath)
                ? path
                : menuPath.Replace("Rows/", "").Replace("/", ".");
        }

        public static bool TryFormatTablePath(string path, out string display)
        {
            display = "";

            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("rows[", StringComparison.Ordinal)) return false;

            int open = path.IndexOf('[', StringComparison.Ordinal);
            int close = FindUnescaped(path, ']', open + 1);

            if (open < 0 || close < 0 || close + 2 > path.Length || path[close + 1] != '.') return false;

            string token = path.Substring(open + 1, close - open - 1);
            int equal = FindUnescaped(token, '=', 0);

            if (equal < 0) return false;

            string rowValue = UnescapePathToken(token[(equal + 1)..]);
            string field = path[(close + 2)..];

            display = $"{rowValue}.{field}";
            return true;
        }

        public static int FindUnescaped(string text, char target, int start)
        {
            if (string.IsNullOrEmpty(text)) return -1;

            for (int i = Mathf.Max(0, start); i < text.Length; i++)
            {
                if (text[i] == '\\')
                {
                    i++;
                    continue;
                }

                if (text[i] == target) return i;
            }

            return -1;
        }

        public static string ValueRecordGroup(string path, DataSourceValueRecord record)
        {
            if (record != null && !string.IsNullOrWhiteSpace(record.Group)) return NicifyPathRoot(record.Group.Replace("[]", ""));

            int index = path.IndexOf('.');
            string root = index < 0 ? "Fields" : path[..index];

            root = root.Replace("[]", "").Replace("[*]", "").Replace("[0]", "");
            return NicifyPathRoot(root);
        }

        public static string ValueRecordName(string path, DataSourceValueRecord record)
        {
            if (record != null && !string.IsNullOrWhiteSpace(record.Name)) return record.Name;

            int index = path.LastIndexOf('.');
            return index < 0 ? path : path[(index + 1)..];
        }

        public static List<DataSourceValueRecord> ParseJsonToValueRecords(string text)
        {
            object root = ParseJsonToObject(text);
            List<DataSourceValueRecord> records = new();
            AddJsonObjectToValueRecords(records, root, "", "Root");
            return records;
        }

        static void AddJsonObjectToValueRecords(List<DataSourceValueRecord> records, object value, string path, string name)
        {
            if (value is Dictionary<string, object> map)
            {
                foreach (KeyValuePair<string, object> pair in map)
                {
                    string childPath = string.IsNullOrEmpty(path) ? pair.Key : $"{path}.{pair.Key}";
                    AddJsonObjectToValueRecords(records, pair.Value, childPath, pair.Key);
                }

                return;
            }

            if (value is List<object> list)
            {
                string arrayPath = string.IsNullOrEmpty(path) ? "[]" : $"{path}[]";

                if (list.Count == 0)
                {
                    records.Add(new DataSourceValueRecord { Name = name, Path = arrayPath, Type = "array", Format = DataSourceValueFormat.Json });
                    return;
                }

                AddJsonObjectToValueRecords(records, list[0], arrayPath, name);
                return;
            }

            records.Add(new DataSourceValueRecord
            {
                Name = name,
                Path = path,
                Type = GetObjectValueType(value),
                Value = ObjectToString(value),
                Group = ValueRecordGroup(path, null),
                MenuPath = $"{ValueRecordGroup(path, null)}/{ValueRecordName(path, null)}",
                Format = DataSourceValueFormat.Json
            });
        }

        public static List<DataSourceValueRecord> ParseXmlToValueRecords(string text)
        {
            XDocument document = XDocument.Parse(text);
            List<DataSourceValueRecord> records = new();

            if (document.Root != null)
                AddXmlElementToValueRecords(records, document.Root, document.Root.Name.LocalName);

            return records;
        }

        static void AddXmlElementToValueRecords(List<DataSourceValueRecord> records, XElement element, string path)
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                string attributePath = $"{path}.@{attribute.Name.LocalName}";

                records.Add(new DataSourceValueRecord
                {
                    Name = attribute.Name.LocalName,
                    Path = attributePath,
                    Type = GuessStringValueType(attribute.Value),
                    Value = attribute.Value,
                    Group = ValueRecordGroup(attributePath, null),
                    MenuPath = $"{ValueRecordGroup(attributePath, null)}/{attribute.Name.LocalName}",
                    Format = DataSourceValueFormat.Xml
                });
            }

            List<XElement> children = element.Elements().ToList();

            if (children.Count == 0)
            {
                records.Add(new DataSourceValueRecord
                {
                    Name = element.Name.LocalName,
                    Path = path,
                    Type = GuessStringValueType(element.Value),
                    Value = element.Value,
                    Group = ValueRecordGroup(path, null),
                    MenuPath = $"{ValueRecordGroup(path, null)}/{element.Name.LocalName}",
                    Format = DataSourceValueFormat.Xml
                });

                return;
            }

            Dictionary<string, int> counts = children.GroupBy(x => x.Name.LocalName).ToDictionary(x => x.Key, x => x.Count());

            foreach (XElement child in children)
            {
                string childName = child.Name.LocalName;
                string childPath = counts[childName] > 1 ? $"{path}.{childName}[]" : $"{path}.{childName}";
                AddXmlElementToValueRecords(records, child, childPath);
            }
        }

        public static string GuessStringValueType(string value)
        {
            if (ParseStringToBool(value, out _)) return "bool";
            if (ParseStringToInt(value, out _)) return "int";
            if (ParseStringToFloat(value, out _)) return "float";
            if (ParseStringToVector2(value, out _)) return "Vector2";
            if (ParseStringToVector3(value, out _)) return "Vector3";
            if (ParseStringToVector4(value, out _)) return "Vector4";
            if (ParseStringToColor(value, out _)) return "Color";
            if (ParseStringToQuaternion(value, out _)) return "Quaternion";
            return "string";
        }

        public static string GetObjectValueType(object value)
        {
            return value switch
            {
                null => "null",
                bool => "bool",
                int => "int",
                long => "int",
                float => "float",
                double d => Math.Abs(d % 1) <= double.Epsilon ? "int" : "float",
                decimal => "float",
                string text => GuessStringValueType(text),
                _ => value.GetType().Name
            };
        }

        public static bool CanAssignValueType(string targetType, string valueType)
        {
            targetType = NormalizeValueType(targetType);
            valueType = NormalizeValueType(valueType);

            if (string.IsNullOrWhiteSpace(targetType) || string.IsNullOrWhiteSpace(valueType)) return true;
            if (targetType == valueType) return true;
            if (targetType == "float" && valueType == "int") return true;
            if (targetType == "double" && (valueType == "float" || valueType == "int")) return true;
            if (targetType == "string") return true;

            return false;
        }

        public static string NormalizeValueType(string value)
        {
            value = value?.Trim().ToLowerInvariant() ?? "";

            return value switch
            {
                "integer" => "int",
                "long" => "int",
                "single" => "float",
                "number" => "float",
                "double" => "float",
                "boolean" => "bool",
                "text" => "string",
                _ => value
            };
        }

        public static string NormalizePathName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        public static string GetLeafPathName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";

            string value = path.Replace("[]", "");
            int dot = value.LastIndexOf('.');
            int slash = value.LastIndexOf('/');
            int index = Mathf.Max(dot, slash);

            return index < 0 ? value : value[(index + 1)..];
        }

        public static object ParseJsonToObject(string json) => string.IsNullOrWhiteSpace(json) ? null : new JsonObjectParser(json).ParseValue();

        static string ReadDictionaryString(Dictionary<string, object> dictionary, string key)
        {
            return dictionary != null && dictionary.TryGetValue(key, out object value) ? ObjectToString(value) : "";
        }

        static string ObjectToString(object value)
        {
            return value switch
            {
                null => "",
                string text => text,
                double number => number.ToString(CultureInfo.InvariantCulture),
                float number => number.ToString(CultureInfo.InvariantCulture),
                int number => number.ToString(CultureInfo.InvariantCulture),
                long number => number.ToString(CultureInfo.InvariantCulture),
                bool boolean => boolean ? "true" : "false",
                _ => value.ToString()
            };
        }

        static string NicifyPathRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            string result = "";
            char previous = '\0';

            foreach (char c in value)
            {
                if (result.Length > 0 && char.IsUpper(c) && previous != '_' && !char.IsWhiteSpace(previous))
                    result += " ";

                result += c == '_' ? ' ' : c;
                previous = c;
            }

            return result;
        }

        class JsonObjectParser
        {
            readonly string json;
            int index;

            public JsonObjectParser(string json) => this.json = json;

            public object ParseValue()
            {
                SkipWhiteSpace();

                if (index >= json.Length) return null;

                char c = json[index];

                if (c == '{') return ParseJsonObject();
                if (c == '[') return ParseJsonArray();
                if (c == '"') return ParseJsonString();
                if (c == 't' || c == 'f') return ParseJsonBool();
                if (c == 'n') return ParseJsonNull();

                return ParseJsonNumber();
            }

            Dictionary<string, object> ParseJsonObject()
            {
                Dictionary<string, object> map = new();
                index++;

                while (true)
                {
                    SkipWhiteSpace();
                    if (Peek('}')) { index++; return map; }

                    string key = ParseJsonString();
                    SkipWhiteSpace();
                    Consume(':');

                    map[key] = ParseValue();

                    SkipWhiteSpace();
                    if (Peek(',')) { index++; continue; }
                    if (Peek('}')) { index++; return map; }

                    return map;
                }
            }

            List<object> ParseJsonArray()
            {
                List<object> list = new();
                index++;

                while (true)
                {
                    SkipWhiteSpace();
                    if (Peek(']')) { index++; return list; }

                    list.Add(ParseValue());

                    SkipWhiteSpace();
                    if (Peek(',')) { index++; continue; }
                    if (Peek(']')) { index++; return list; }

                    return list;
                }
            }

            string ParseJsonString()
            {
                Consume('"');
                StringBuilder result = new();

                while (index < json.Length)
                {
                    char c = json[index++];

                    if (c == '"') return result.ToString();

                    if (c != '\\')
                    {
                        result.Append(c);
                        continue;
                    }

                    if (index >= json.Length) return result.ToString();

                    char escaped = json[index++];

                    result.Append(escaped switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'b' => '\b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'u' => ParseJsonUnicode(),
                        _ => escaped
                    });
                }

                return result.ToString();
            }

            char ParseJsonUnicode()
            {
                string hex = json.Substring(index, 4);
                index += 4;
                return (char)Convert.ToInt32(hex, 16);
            }

            bool ParseJsonBool()
            {
                if (json.Substring(index).StartsWith("true"))
                {
                    index += 4;
                    return true;
                }

                index += 5;
                return false;
            }

            object ParseJsonNull()
            {
                index += 4;
                return null;
            }

            object ParseJsonNumber()
            {
                int start = index;

                while (index < json.Length && "-+0123456789.eE".IndexOf(json[index]) >= 0)
                    index++;

                string number = json[start..index];

                if (!number.Contains('.') &&
                    !number.Contains('e') &&
                    !number.Contains('E') &&
                    long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue))
                    return longValue;

                double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue);
                return doubleValue;
            }

            void SkipWhiteSpace()
            {
                while (index < json.Length && char.IsWhiteSpace(json[index]))
                    index++;
            }

            bool Peek(char c) => index < json.Length && json[index] == c;

            void Consume(char c)
            {
                SkipWhiteSpace();

                if (index < json.Length && json[index] == c)
                    index++;
            }
        }
    }
}
#endif
