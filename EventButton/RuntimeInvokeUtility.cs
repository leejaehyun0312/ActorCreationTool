#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.Utility
{
    public static class RuntimeInvokeUtility
    {
        public static void Invoke(EventButton sender, string data)
        {
            MethodInvoker invoker = data;
            if (invoker.IsEmpty) return;

            UnityEngine.Object target = LoadAssetByGuid(invoker.TargetGuid, typeof(UnityEngine.Object));
            if (target == null)
            {
                Debug.LogWarning($"Invoke 실패: Target을 찾을 수 없습니다. Button={sender.name}, TargetGuid={invoker.TargetGuid}, Method={invoker.Method}");
                return;
            }

            MethodInfo method = FindMethod(target, invoker.Method);
            if (method == null)
            {
                Debug.LogWarning($"Invoke 실패: 메서드를 찾을 수 없습니다. Target={target.GetType().Name}, Method={invoker.Method}");
                PrintSupportedMethods(target);
                return;
            }

            ParameterInfo[] parameters = method.GetParameters();

            try
            {
                if (parameters.Length == 0) { method.Invoke(target, null); return; }

                if (TryConvertArgument(invoker.Argument, parameters[0].ParameterType, sender, out object value))
                {
                    method.Invoke(target, new[] { value });
                    return;
                }

                Debug.LogWarning($"Invoke 실패: 인자 변환 실패. Method={invoker.Method}, Argument={invoker.Argument}");
            }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static MethodInfo FindMethod(object target, string signature) => target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(IsBaseSupported)
            .FirstOrDefault(method => GetSignature(method) == signature);

        public static bool IsSupported(MethodInfo method)
        {
            if (!IsBaseSupported(method)) return false;

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 0 || parameters.Length == 1 && IsSupportedParameter(parameters[0].ParameterType);
        }

        static bool IsBaseSupported(MethodInfo method) =>
            !method.IsSpecialName && !method.IsStatic && !method.IsGenericMethod && method.ReturnType == typeof(void);

        public static bool IsSupportedParameter(Type type) =>
            type == typeof(string) ||
            type == typeof(int) ||
            type == typeof(long) ||
            type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(bool) ||
            type.IsEnum ||
            typeof(VisualElement).IsAssignableFrom(type) ||
            typeof(UnityEngine.Object).IsAssignableFrom(type);

        public static bool TryConvertArgument(string text, Type type, EventButton sender, out object value)
        {
            value = null;

            if (type == typeof(string)) { value = text ?? ""; return true; }
            if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)) { value = intValue; return true; }
            if (type == typeof(long) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue)) { value = longValue; return true; }
            if (type == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue)) { value = floatValue; return true; }
            if (type == typeof(double) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue)) { value = doubleValue; return true; }
            if (type == typeof(bool) && bool.TryParse(text, out bool boolValue)) { value = boolValue; return true; }
            if (type.IsEnum && Enum.TryParse(type, text, true, out object enumValue)) { value = enumValue; return true; }
            if (typeof(VisualElement).IsAssignableFrom(type)) { value = sender; return true; }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                value = LoadAssetByGuid(text, type);
                return value != null;
            }

            return false;
        }

        public static string GetLabel(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 0 ? $"{method.Name}()" : $"{method.Name}({parameters[0].ParameterType.Name})";
        }

        public static string GetSignature(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 0 ? $"{method.Name}()" : $"{method.Name}({parameters[0].ParameterType.FullName})";
        }

        public static string GetShortMethodName(string signature)
        {
            if (string.IsNullOrWhiteSpace(signature)) return "None";

            int index = signature.IndexOf('(');
            if (index < 0) return signature;

            string methodName = signature[..index];
            string parameter = signature[(index + 1)..].TrimEnd(')');
            return string.IsNullOrWhiteSpace(parameter) ? $"{methodName}()" : $"{methodName}({parameter.Split('.').Last()})";
        }

        public static string GetDisplayName(string data)
        {
            MethodInvoker invoker = data;
            if (invoker.IsEmpty) return "Not Bound";

            UnityEngine.Object target = LoadAssetByGuid(invoker.TargetGuid, typeof(UnityEngine.Object));
            string targetName = target == null ? "Missing Target" : target.name;
            return $"Bound: {targetName}.{GetShortMethodName(invoker.Method)}";
        }

        public static void PrintSupportedMethods(object target)
        {
            var methods = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(IsSupported)
                .Select(GetSignature);

            Debug.Log($"지원 가능한 메서드 목록 ({target.GetType().Name})\n{string.Join("\n", methods)}");
        }

        public static string GetGuid(UnityEngine.Object asset)
        {
            if (asset == null) return "";

            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrWhiteSpace(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        }

        public static UnityEngine.Object LoadAssetByGuid(string guid, Type type)
        {
            if (string.IsNullOrWhiteSpace(guid)) return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path)) return null;

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            return asset != null && type.IsInstanceOfType(asset) ? asset : null;
        }
    }
}
#endif
