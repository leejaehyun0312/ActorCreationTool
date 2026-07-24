using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ACT.Utiltiy
{
    public static class RuntimeInvokeUtility
        {
            public static void InvokeTarget(object fallbackTarget, EventButton sender, MethodInvoker invoker)
            {
                if (invoker.IsEmpty) return;

                var target = ResolveTarget(invoker, fallbackTarget);
                if (target == null)
                {
                    Debug.LogWarning($"Invoke 실패: Target을 찾을 수 없습니다. Button={sender?.name}, TargetGuid={invoker.TargetGuid}, Method={invoker.Method}");
                    return;
                }

                var method = FindMethod(target, invoker.Method);
                if (method == null)
                {
                    Debug.LogWarning($"Invoke 실패: 메서드를 찾을 수 없습니다. Target={target.GetType().Name}, Method={invoker.Method}");
                    PrintSupportedMethods(target);
                    return;
                }

                var parameters = method.GetParameters();
                try
                {
                    if (parameters.Length == 0)
                    {
                        method.Invoke(target, null);
                        return;
                    }

                    if (TryConvertArgument(invoker.Argument, parameters[0].ParameterType, sender, target, out var value))
                    {
                        method.Invoke(target, new[] { value });
                        return;
                    }

                    Debug.LogWarning($"Invoke 실패: 인자 변환 실패. Method={invoker.Method}, Argument={invoker.Argument}");
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            public static object ResolveTarget(MethodInvoker invoker, object fallbackTarget)
            {
    #if UNITY_EDITOR
                if (!string.IsNullOrWhiteSpace(invoker.TargetGuid))
                {
                    var asset = LoadAssetByGuid(invoker.TargetGuid, typeof(UnityEngine.Object));
                    if (asset != null) return asset;
                }
    #endif
                return fallbackTarget;
            }

            public static MethodInfo FindMethod(object target, string signature) =>
                target.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(IsBaseSupported)
                    .FirstOrDefault(x => GetSignature(x) == signature || x.Name == signature);

            public static bool IsSupported(MethodInfo method)
            {
                if (!IsBaseSupported(method)) return false;
                var parameters = method.GetParameters();
                return parameters.Length == 0 || parameters.Length == 1 && IsSupportedParameter(parameters[0].ParameterType);
            }

            static bool IsBaseSupported(MethodInfo method) =>
                !method.IsSpecialName &&
                !method.IsStatic &&
                !method.IsGenericMethod &&
                method.ReturnType == typeof(void);

            public static bool IsSupportedParameter(Type type) =>
                type == typeof(string) ||
                type == typeof(int) ||
                type == typeof(long) ||
                type == typeof(float) ||
                type == typeof(double) ||
                type == typeof(bool) ||
                type == typeof(Vector2) ||
                type == typeof(Vector3) ||
                type == typeof(Vector4) ||
                type == typeof(Vector2Int) ||
                type == typeof(Vector3Int) ||
                type == typeof(Rect) ||
                type == typeof(RectInt) ||
                type == typeof(Bounds) ||
                type == typeof(BoundsInt) ||
                type == typeof(Color) ||
                type == typeof(Gradient) ||
                type == typeof(AnimationCurve) ||
                type.IsEnum ||
                typeof(VisualElement).IsAssignableFrom(type) ||
                typeof(UnityEngine.Object).IsAssignableFrom(type);

            public static bool TryConvertArgument(string text, Type type, EventButton sender, object target, out object value)
            {
                value = null;

                if (type == typeof(string))
                {
                    value = text ?? "";
                    return true;
                }
                if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                {
                    value = intValue;
                    return true;
                }
                if (type == typeof(long) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                {
                    value = longValue;
                    return true;
                }
                if (type == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    value = floatValue;
                    return true;
                }
                if (type == typeof(double) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
                {
                    value = doubleValue;
                    return true;
                }
                if (type == typeof(bool) && bool.TryParse(text, out var boolValue))
                {
                    value = boolValue;
                    return true;
                }
                if (type.IsEnum && Enum.TryParse(type, text, true, out var enumValue))
                {
                    value = enumValue;
                    return true;
                }
                if (typeof(VisualElement).IsAssignableFrom(type))
                {
                    value = sender;
                    return true;
                }

    #if UNITY_EDITOR
                if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                {
                    var asset = LoadAssetByGuid(text, type);
                    if (asset != null)
                    {
                        value = asset;
                        return true;
                    }
                }
    #endif

                if (typeof(UnityEngine.Object).IsAssignableFrom(type) && type.IsInstanceOfType(target))
                {
                    value = target;
                    return true;
                }

                return false;
            }

            public static string GetLabel(MethodInfo method)
            {
                var parameters = method.GetParameters();
                return parameters.Length == 0 ? $"{method.Name}()" : $"{method.Name}({parameters[0].ParameterType.Name})";
            }

            public static string GetSignature(MethodInfo method)
            {
                var parameters = method.GetParameters();
                return parameters.Length == 0 ? $"{method.Name}()" : $"{method.Name}({parameters[0].ParameterType.FullName})";
            }

            public static string GetShortMethodName(string signature)
            {
                if (string.IsNullOrWhiteSpace(signature)) return "None";

                var index = signature.IndexOf('(');
                if (index < 0) return signature;

                var methodName = signature[..index];
                var param = signature[(index + 1)..].TrimEnd(')');

                return string.IsNullOrWhiteSpace(param) ? $"{methodName}()" : $"{methodName}({param.Split('.').Last()})";
            }

            public static string GetDisplayName(MethodInvoker invoker)
            {
    #if UNITY_EDITOR
                if (invoker.IsEmpty) return "Not Bound";

                var target = LoadAssetByGuid(invoker.TargetGuid, typeof(UnityEngine.Object));
                var targetName = target == null ? "Fallback Target" : target.name;

                return $"Bound: {targetName}.{GetShortMethodName(invoker.Method)}";
    #else
                return invoker.IsEmpty ? "Not Bound" : $"Bound: {GetShortMethodName(invoker.Method)}";
    #endif
            }

            public static void PrintSupportedMethods(object target)
            {
                var methods = target.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(IsSupported)
                    .Select(GetSignature);

                Debug.Log($"지원 가능한 메서드 목록 ({target.GetType().Name})\n{string.Join("\n", methods)}");
            }

    #if UNITY_EDITOR
            public static string GetGuid(UnityEngine.Object asset)
            {
                if (asset == null) return "";

                var path = AssetDatabase.GetAssetPath(asset);
                return string.IsNullOrWhiteSpace(path) ? "" : AssetDatabase.AssetPathToGUID(path);
            }

            public static UnityEngine.Object LoadAssetByGuid(string guid, Type type)
            {
                if (string.IsNullOrWhiteSpace(guid)) return null;

                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path)) return null;

                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                return asset != null && type.IsInstanceOfType(asset) ? asset : null;
            }
    #endif
        }
}
