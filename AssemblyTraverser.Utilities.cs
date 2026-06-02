using System;
using System.Text.RegularExpressions;
using NXOpen;
using NXOpen.Assemblies;

/// <summary>
/// 装配遍历器工具类 - 提供通用工具方法
/// </summary>
public static class AssemblyTraverserUtils
{
    /// <summary>
    /// 获取零件的用户友好名称（优先使用Teamcenter的 DB_PART_NAME，否则使用显示名）
    /// </summary>
    public static string GetDescriptiveName(Component comp)
    {
        Part part = comp.Prototype as Part;
        if (part != null)
        {
            try
            {
                // 获取"名称" (DB_PART_NAME)，而不是"描述"
                string name = part.GetUserAttribute("DB_PART_NAME", NXObject.AttributeType.String, -1).StringValue;
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            catch
            {
                // 属性不存在则忽略异常
            }
        }
        // 回退方案：使用组件的显示名
        return comp.DisplayName;
    }

    /// <summary>
    /// 判断名称是否与给定的通配符模式之一匹配。
    /// 支持通配符：* (任意字符序列) 和 ? (单个字符)。
    /// </summary>
    public static bool IsMatching(string name, string pattern)
    {
        // 将通配符转换为正则表达式
        // 1. 转义除了 * 和 ? 之外的所有正则特殊字符
        string regexPattern = "^" + Regex.Escape(pattern)
                                    .Replace("\\*", ".*")
                                    .Replace("\\?", ".") + "$";
        return Regex.IsMatch(name, regexPattern);
    }

    /// <summary>
    /// 重载：判断文本是否匹配给定的通配符模式数组中的任意一个。
    /// 如果数组为空或 null，返回 false。
    /// </summary>
    public static bool IsMatching(string text, string[] patterns)
    {
        if (patterns == null || patterns.Length == 0)
            return false;

        foreach (string pattern in patterns)
        {
            if (IsMatching(text, pattern))   // 调用单模式版本
                return true;
        }
        return false;
    }
}
