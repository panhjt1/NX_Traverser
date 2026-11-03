using NXOpen;
using NXOpen.Assemblies;

/// <summary>
/// 事务处理器接口 - 所有事务处理器必须实现此接口
/// </summary>
public interface ITransactionHandler
{
    /// <summary>
    /// 对匹配的组件执行具体事务操作
    /// </summary>
    /// <param name="comp">当前组件</param>
    /// <param name="theSession">NX会话</param>
    /// <param name="outputFolder">输出文件夹路径</param>
    /// <param name="descriptiveName">描述性名称</param>
    void Process(Component comp, Session theSession, string outputFolder, string descriptiveName);

    /// <summary>
    /// 事务结束后的资源清理（如关闭文件等）
    /// </summary>
    void Finish();
}
