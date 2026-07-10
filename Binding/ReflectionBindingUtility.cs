#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ACT
{
    public static class ReflectionBindingUtility
    {
        public sealed class MemberDescriptor
        {
            public string Name;
            public Type ValueType;
            public MemberInfo Member;
        }

        public static List<MemberDescriptor> GetReadableMembers(Type type)
        {
            List<MemberDescriptor> result = new();

            if (type == null) return result;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo[] fields = type.GetFields(flags);

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];

                if (field.IsStatic) continue;
                if (field.IsNotSerialized) continue;
                if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null) continue;

                result.Add(new MemberDescriptor { Name = field.Name, ValueType = field.FieldType, Member = field });
            }

            PropertyInfo[] properties = type.GetProperties(flags);

            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];

                if (property.GetIndexParameters().Length > 0) continue;
                if (!property.CanRead) continue;

                MethodInfo getter = property.GetGetMethod(true);

                if (getter == null) continue;
                if (getter.IsStatic) continue;
                if (!getter.IsPublic) continue;

                result.Add(new MemberDescriptor { Name = property.Name, ValueType = property.PropertyType, Member = property });
            }

            result.Sort((a, b) => a.Member.MetadataToken.CompareTo(b.Member.MetadataToken));
            return result;
        }

        public static object GetValue(object target, string path)
        {
            if (target == null) return null;
            if (string.IsNullOrEmpty(path)) return target;

            object current = target;
            string[] segments = path.Split('.');

            for (int i = 0; i < segments.Length; i++)
            {
                if (current == null) return null;
                current = GetSegmentValue(current, segments[i]);
            }

            return current;
        }

        public static bool SetValue(object target, string path, object value)
        {
            if (target == null || string.IsNullOrEmpty(path)) return false;

            string[] segments = path.Split('.');
            object current = target;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                current = GetSegmentValue(current, segments[i]);
                if (current == null) return false;
            }

            return SetSegmentValue(current, segments[^1], value);
        }

        public static Type GetValueType(object target, string path)
        {
            if (target == null) return typeof(object);
            if (string.IsNullOrEmpty(path)) return target.GetType();

            Type currentType = target.GetType();
            string[] segments = path.Split('.');

            for (int i = 0; i < segments.Length; i++)
            {
                currentType = GetSegmentType(currentType, segments[i]);
                if (currentType == null) return typeof(object);
            }

            return currentType;
        }

        public static object GetIndexedValue(object target, string path, int index)
        {
            if (target == null || string.IsNullOrEmpty(path)) return null;

            string[] segments = path.Split('.');

            if (segments.Length == 1) return GetValue(target, $"{segments[0]}[{index}]");

            object list = GetValue(target, segments[0]);
            object element = GetIndexedObject(list, index);

            if (element == null) return null;

            string childPath = string.Join(".", segments, 1, segments.Length - 1);
            return GetValue(element, childPath);
        }

        public static bool SetIndexedValue(object target, string path, int index, object value)
        {
            if (target == null || string.IsNullOrEmpty(path)) return false;

            string[] segments = path.Split('.');

            if (segments.Length == 1) return SetValue(target, $"{segments[0]}[{index}]", value);

            object list = GetValue(target, segments[0]);
            object element = GetIndexedObject(list, index);

            if (element == null) return false;

            string childPath = string.Join(".", segments, 1, segments.Length - 1);
            return SetValue(element, childPath, value);
        }

        public static Type GetIndexedValueType(object target, string path, int index)
        {
            if (target == null || string.IsNullOrEmpty(path)) return typeof(object);

            string[] segments = path.Split('.');

            if (segments.Length == 1) return GetValueType(target, $"{segments[0]}[{index}]");

            object list = GetValue(target, segments[0]);
            object element = GetIndexedObject(list, index);

            if (element == null) return typeof(object);

            string childPath = string.Join(".", segments, 1, segments.Length - 1);
            return GetValueType(element, childPath);
        }

        static object GetSegmentValue(object target, string segment)
        {
            ParseSegment(segment, out string name, out int index);

            object value = GetMemberValue(target, name);
            if (index >= 0) value = GetIndexedObject(value, index);

            return value;
        }

        static bool SetSegmentValue(object target, string segment, object value)
        {
            ParseSegment(segment, out string name, out int index);

            if (index >= 0)
            {
                object collection = GetMemberValue(target, name);
                return SetIndexedObject(collection, index, value);
            }

            MemberInfo member = GetMember(target.GetType(), name);
            if (member == null) return false;

            Type memberType = GetMemberType(member);
            object converted = ConvertValue(value, memberType);

            if (member is FieldInfo field)
            {
                field.SetValue(target, converted);
                return true;
            }

            if (member is PropertyInfo property && property.CanWrite)
            {
                property.SetValue(target, converted);
                return true;
            }

            return false;
        }

        static Type GetSegmentType(Type type, string segment)
        {
            ParseSegment(segment, out string name, out int index);

            MemberInfo member = GetMember(type, name);
            if (member == null) return typeof(object);

            Type memberType = GetMemberType(member);
            return index >= 0 ? GetElementType(memberType) : memberType;
        }

        static object GetMemberValue(object target, string name)
        {
            if (target == null) return null;

            MemberInfo member = GetMember(target.GetType(), name);
            if (member == null) return null;

            if (member is FieldInfo field) return field.GetValue(target);
            if (member is PropertyInfo property && property.CanRead) return property.GetValue(target);

            return null;
        }

        static MemberInfo GetMember(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo field = type.GetField(name, flags);
            if (field != null) return field;

            return type.GetProperty(name, flags);
        }

        static Type GetMemberType(MemberInfo member) =>
            member is FieldInfo field ? field.FieldType :
            member is PropertyInfo property ? property.PropertyType :
            typeof(object);

        static object GetIndexedObject(object collection, int index)
        {
            if (collection == null) return null;

            if (collection is IList list)
            {
                if (index < 0 || index >= list.Count) return null;
                return list[index];
            }

            if (collection is Array array)
            {
                if (index < 0 || index >= array.Length) return null;
                return array.GetValue(index);
            }

            return null;
        }

        static bool SetIndexedObject(object collection, int index, object value)
        {
            if (collection == null) return false;

            if (collection is IList list)
            {
                if (index < 0 || index >= list.Count) return false;

                Type elementType = GetElementType(collection.GetType());
                list[index] = ConvertValue(value, elementType);
                return true;
            }

            if (collection is Array array)
            {
                if (index < 0 || index >= array.Length) return false;

                Type elementType = array.GetType().GetElementType();
                array.SetValue(ConvertValue(value, elementType), index);
                return true;
            }

            return false;
        }

        static Type GetElementType(Type collectionType)
        {
            if (collectionType == null) return typeof(object);
            if (collectionType.IsArray) return collectionType.GetElementType();

            return collectionType.IsGenericType ? collectionType.GetGenericArguments()[0] : typeof(object);
        }

        static void ParseSegment(string segment, out string name, out int index)
        {
            name = segment;
            index = -1;

            int open = segment.IndexOf('[');
            int close = segment.IndexOf(']');

            if (open < 0 || close < 0 || close <= open) return;

            name = segment[..open];

            if (int.TryParse(segment.Substring(open + 1, close - open - 1), out int parsed)) index = parsed;
        }

        public static void InvokeMethod(object target, string methodName, params object[] optionalArgs)
        {
            if (target == null || string.IsNullOrEmpty(methodName)) return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo method = target.GetType().GetMethod(methodName, flags);

            if (method == null) return;

            ParameterInfo[] parameters = method.GetParameters();

            if (parameters.Length == 0)
            {
                method.Invoke(target, null);
                return;
            }

            object[] args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                object matched = null;

                for (int j = 0; j < optionalArgs.Length; j++)
                {
                    if (optionalArgs[j] == null) continue;

                    if (parameters[i].ParameterType.IsInstanceOfType(optionalArgs[j]))
                    {
                        matched = optionalArgs[j];
                        break;
                    }
                }

                args[i] = matched ?? GetDefaultValue(parameters[i].ParameterType);
            }

            method.Invoke(target, args);
        }

        public static object ConvertValue(object value, Type targetType)
        {
            if (targetType == null || targetType == typeof(object)) return value;

            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (value == null) return GetDefaultValue(targetType);
            if (targetType.IsInstanceOfType(value)) return value;

            if (targetType.IsEnum)
            {
                if (value is string enumText) return Enum.Parse(targetType, enumText);
                return Enum.ToObject(targetType, value);
            }

            if (targetType == typeof(string)) return value.ToString();

            if (targetType == typeof(byte))
            {
                if (byte.TryParse(value.ToString(), out byte parsed)) return parsed;
                return 0;
            }

            if (targetType == typeof(sbyte))
            {
                if (sbyte.TryParse(value.ToString(), out sbyte parsed)) return parsed;
                return 0;
            }

            if (targetType == typeof(short))
            {
                if (short.TryParse(value.ToString(), out short parsed)) return parsed;
                return 0;
            }

            if (targetType == typeof(ushort))
            {
                if (ushort.TryParse(value.ToString(), out ushort parsed)) return parsed;
                return 0;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(value.ToString(), out int parsed)) return parsed;
                return 0;
            }

            if (targetType == typeof(uint))
            {
                if (uint.TryParse(value.ToString(), out uint parsed)) return parsed;
                return 0;
            }

            if (targetType == typeof(long))
            {
                if (long.TryParse(value.ToString(), out long parsed)) return parsed;
                return 0L;
            }

            if (targetType == typeof(ulong))
            {
                if (ulong.TryParse(value.ToString(), out ulong parsed)) return parsed;
                return 0UL;
            }

            if (targetType == typeof(float))
            {
                if (float.TryParse(value.ToString(), out float parsed)) return parsed;
                return 0f;
            }

            if (targetType == typeof(double))
            {
                if (double.TryParse(value.ToString(), out double parsed)) return parsed;
                return 0d;
            }

            if (targetType == typeof(decimal))
            {
                if (decimal.TryParse(value.ToString(), out decimal parsed)) return parsed;
                return 0m;
            }

            if (targetType == typeof(bool))
            {
                if (bool.TryParse(value.ToString(), out bool parsed)) return parsed;
                return false;
            }

            return value;
        }

        public static object GetDefaultValue(Type type) =>
            type == null ? null :
            type == typeof(string) ? "" :
            type.IsValueType ? Activator.CreateInstance(type) :
            null;
    }
}
#endif