// netstandard2.0 缺少 System.Runtime.CompilerServices.IsExternalInit（record / init 访问器编译需要）。
// 目标为 .NET 5+ 时由 BCL 提供，此 polyfill 自动跳过，不影响其他目标框架。
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
