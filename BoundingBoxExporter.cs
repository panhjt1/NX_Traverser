using System;
using System.IO;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

/// <summary>
/// 包容体尺寸导出事务类 - 查询部件包围盒尺寸并写入CSV文件
/// </summary>
public class BoundingBoxExporter : ITransactionHandler
{
    private static readonly string CsvFileName = "BoundingBoxResult.csv";
    
    private string _csysMode;
    private StreamWriter _csvWriter;
    private bool _headerWritten;

    public BoundingBoxExporter(string csysMode)
    {
        _csysMode = string.IsNullOrEmpty(csysMode) ? "ACS" : csysMode;
        _csvWriter = null;
        _headerWritten = false;
    }

    /// <summary>
    /// 处理包容体尺寸导出事务
    /// </summary>
    public void Process(Component comp, Session theSession, string outputFolder, string descriptiveName)
    {
        Part part = comp.Prototype as Part;
        if (part == null)
        {
            theSession.ListingWindow.WriteLine(string.Format("    【错误】无法获取零件原型: {0}", comp.DisplayName));
            return;
        }

        try
        {
            // 计算实体数量
            int bodyCount = 0;
            BodyCollection bodies = part.Bodies;
            foreach (Body body in bodies)
            {
                bodyCount++;
            }

            // 检查并记录加载状态
            theSession.ListingWindow.WriteLine(string.Format("    正在处理: {0} | 完全加载={1} | 实体数={2}",
                part.Name, part.IsFullyLoaded, bodyCount));

            // 如果不是完全加载，尝试强制加载
            if (!part.IsFullyLoaded)
            {
                theSession.ListingWindow.WriteLine(string.Format("    -> 尝试加载几何数据..."));
                try
                {
                    part.LoadThisPartFully();
                    // 重新计算实体数量
                    bodyCount = 0;
                    foreach (Body body in bodies)
                    {
                        bodyCount++;
                    }
                    theSession.ListingWindow.WriteLine(string.Format("    -> 加载完成，实体数={0}", bodyCount));
                }
                catch (Exception loadEx)
                {
                    theSession.ListingWindow.WriteLine(string.Format("    -> 加载失败: {0}", loadEx.Message));
                }
            }

            // 获取包围盒尺寸
            double[] dimensions = GetBoundingBoxDimensions(part, theSession);
            if (dimensions == null)
            {
                theSession.ListingWindow.WriteLine(string.Format("    【警告】无法获取包围盒尺寸: {0}", comp.DisplayName));
                return;
            }

            // 写入CSV
            WriteToCsv(outputFolder, comp, dimensions, theSession);
        }
        catch (Exception ex)
        {
            theSession.ListingWindow.WriteLine(string.Format("    【处理失败】{0}: {1}", comp.DisplayName, ex.Message));
        }
    }

    /// <summary>
    /// 根据配置获取包围盒尺寸（入口方法）
    /// </summary>
    private double[] GetBoundingBoxDimensions(Part part, Session theSession)
    {
        switch (_csysMode.ToUpper())
        {
            case "WCS":
                return GetBoundingBoxWCS(part, theSession);
            case "ACS":
            default:
                return GetBoundingBoxACS(part, theSession);
        }
    }

    /// <summary>
    /// 使用 ACS 坐标系 + AskBoundingBoxExact 获取精确包围盒
    /// </summary>
    private double[] GetBoundingBoxACS(Part part, Session theSession)
    {
        var ufSession = UFSession.GetUFSession();

        try
        {
            // 获取 ACS 坐标系 Tag（null 表示 ACS）
            Tag acsTag = Tag.Null;

            // 获取部件中的所有实体(Body)
            BodyCollection bodies = part.Bodies;

            // 初始化整体包围盒
            double overallMinX = double.MaxValue, overallMinY = double.MaxValue, overallMinZ = double.MaxValue;
            double overallMaxX = double.MinValue, overallMaxY = double.MinValue, overallMaxZ = double.MinValue;
            bool hasValidBody = false;

            // 遍历所有实体
            foreach (Body body in bodies)
            {
                if (body.Tag == Tag.Null)
                    continue;

                try
                {
                    // 使用精确版本获取包围盒
                    double[] minCorner = new double[3];
                    double[,] directions = new double[3, 3];
                    double[] distances = new double[3];

                    ufSession.Modl.AskBoundingBoxExact(body.Tag, acsTag, minCorner, directions, distances);

                    // 获取实体名称（用于诊断日志）
                    string bodyName = "";
                    try { bodyName = body.Name; } catch { }
                    
                    // 检查是否有特异值（任一方向绝对值>1e6），有则跳过该实体
                    if (Math.Abs(distances[0]) > 1e6 || Math.Abs(distances[1]) > 1e6 || Math.Abs(distances[2]) > 1e6 ||
                        Math.Abs(minCorner[0]) > 1e6 || Math.Abs(minCorner[1]) > 1e6 || Math.Abs(minCorner[2]) > 1e6)
                    {
                        theSession.ListingWindow.WriteLine(string.Format(
                            "        【跳过特异实体】Tag={0} Name='{1}': X={2:F3} Y={3:F3} Z={4:F3} | 位置({5:F1},{6:F1},{7:F1})",
                            body.Tag, bodyName, distances[0], distances[1], distances[2],
                            minCorner[0], minCorner[1], minCorner[2]));
                        continue;  // 跳过该实体，不参与包围盒计算
                    }

                    // 计算最大角点
                    double maxX = minCorner[0] + distances[0];
                    double maxY = minCorner[1] + distances[1];
                    double maxZ = minCorner[2] + distances[2];

                    // 更新整体包围盒
                    overallMinX = Math.Min(overallMinX, minCorner[0]);
                    overallMinY = Math.Min(overallMinY, minCorner[1]);
                    overallMinZ = Math.Min(overallMinZ, minCorner[2]);
                    overallMaxX = Math.Max(overallMaxX, maxX);
                    overallMaxY = Math.Max(overallMaxY, maxY);
                    overallMaxZ = Math.Max(overallMaxZ, maxZ);

                    hasValidBody = true;
                }
                catch (Exception ex)
                {
                    // 该实体无法获取包围盒（轻量化体），跳过
                    theSession.ListingWindow.WriteLine(string.Format("        跳过实体: {0}", ex.Message));
                    continue;
                }
            }

            // 检查是否有有效实体
            if (!hasValidBody)
            {
                theSession.ListingWindow.WriteLine(string.Format("      没有可获取包围盒的实体（可能都是轻量化体）"));
                return null;
            }

            // 计算 XYZ 尺寸
            double xSize = overallMaxX - overallMinX;
            double ySize = overallMaxY - overallMinY;
            double zSize = overallMaxZ - overallMinZ;

            return new double[] { xSize, ySize, zSize };
        }
        catch (Exception ex)
        {
            theSession.ListingWindow.WriteLine(string.Format("      获取ACS包围盒异常: {0}", ex.Message));
            return null;
        }
    }

    /// <summary>
    /// 使用 WCS 坐标系 + AskBoundingBoxExact + 坐标转换获取精确包围盒
    /// </summary>
    private double[] GetBoundingBoxWCS(Part part, Session theSession)
    {
        var ufSession = UFSession.GetUFSession();

        try
        {
            // 获取 WCS 坐标系 Tag
            Tag wcsTag = part.WCS.Tag;

            // 获取 WCS 原点和矩阵（用于坐标转换）
            double[] wcsOrigin = new double[3];
            Tag wcsMatrixTag;
            ufSession.Csys.AskCsysInfo(wcsTag, out wcsMatrixTag, wcsOrigin);
            double[] wcsMatrix = new double[9];
            ufSession.Csys.AskMatrixValues(wcsMatrixTag, wcsMatrix);
            // wcsMatrix[0-2]=X轴, [3-5]=Y轴, [6-8]=Z轴（在ACS下的分量）

            // 获取部件中的所有实体(Body)
            BodyCollection bodies = part.Bodies;

            // 初始化整体包围盒（在WCS下）
            double overallMinX = double.MaxValue, overallMinY = double.MaxValue, overallMinZ = double.MaxValue;
            double overallMaxX = double.MinValue, overallMaxY = double.MinValue, overallMaxZ = double.MinValue;
            bool hasValidBody = false;

            // 遍历所有实体
            foreach (Body body in bodies)
            {
                if (body.Tag == Tag.Null)
                    continue;

                try
                {
                    // 使用精确版本获取 ACS 下的包围盒
                    double[] minCornerACS = new double[3];
                    double[,] directions = new double[3, 3];
                    double[] distances = new double[3];

                    ufSession.Modl.AskBoundingBoxExact(body.Tag, Tag.Null, minCornerACS, directions, distances);

                    // 获取实体名称（用于诊断日志）
                    string bodyName = "";
                    try { bodyName = body.Name; } catch { }

                    // 检查是否有特异值（任一方向绝对值>1e6），有则跳过该实体
                    if (Math.Abs(distances[0]) > 1e6 || Math.Abs(distances[1]) > 1e6 || Math.Abs(distances[2]) > 1e6 ||
                        Math.Abs(minCornerACS[0]) > 1e6 || Math.Abs(minCornerACS[1]) > 1e6 || Math.Abs(minCornerACS[2]) > 1e6)
                    {
                        theSession.ListingWindow.WriteLine(string.Format(
                            "        【跳过特异实体】Tag={0} Name='{1}': X={2:F3} Y={3:F3} Z={4:F3} | 位置({5:F1},{6:F1},{7:F1})",
                            body.Tag, bodyName, distances[0], distances[1], distances[2],
                            minCornerACS[0], minCornerACS[1], minCornerACS[2]));
                        continue;  // 跳过该实体，不参与包围盒计算
                    }

                    // 计算 ACS 下的最大角点
                    double[] maxCornerACS = new double[3];
                    maxCornerACS[0] = minCornerACS[0] + distances[0];
                    maxCornerACS[1] = minCornerACS[1] + distances[1];
                    maxCornerACS[2] = minCornerACS[2] + distances[2];

                    // 将包围盒的8个顶点从ACS转换到WCS
                    double[][] verticesACS = new double[][]
                    {
                        new double[] { minCornerACS[0], minCornerACS[1], minCornerACS[2] },
                        new double[] { minCornerACS[0], minCornerACS[1], maxCornerACS[2] },
                        new double[] { minCornerACS[0], maxCornerACS[1], minCornerACS[2] },
                        new double[] { minCornerACS[0], maxCornerACS[1], maxCornerACS[2] },
                        new double[] { maxCornerACS[0], minCornerACS[1], minCornerACS[2] },
                        new double[] { maxCornerACS[0], minCornerACS[1], maxCornerACS[2] },
                        new double[] { maxCornerACS[0], maxCornerACS[1], minCornerACS[2] },
                        new double[] { maxCornerACS[0], maxCornerACS[1], maxCornerACS[2] }
                    };

                    foreach (var vertexACS in verticesACS)
                    {
                        // 顶点相对于WCS原点的向量
                        double dx = vertexACS[0] - wcsOrigin[0];
                        double dy = vertexACS[1] - wcsOrigin[1];
                        double dz = vertexACS[2] - wcsOrigin[2];

                        // 用WCS矩阵的逆（转置）转换到WCS坐标
                        double wcsX = wcsMatrix[0] * dx + wcsMatrix[3] * dy + wcsMatrix[6] * dz;
                        double wcsY = wcsMatrix[1] * dx + wcsMatrix[4] * dy + wcsMatrix[7] * dz;
                        double wcsZ = wcsMatrix[2] * dx + wcsMatrix[5] * dy + wcsMatrix[8] * dz;

                        // 更新WCS下的包围盒
                        overallMinX = Math.Min(overallMinX, wcsX);
                        overallMinY = Math.Min(overallMinY, wcsY);
                        overallMinZ = Math.Min(overallMinZ, wcsZ);
                        overallMaxX = Math.Max(overallMaxX, wcsX);
                        overallMaxY = Math.Max(overallMaxY, wcsY);
                        overallMaxZ = Math.Max(overallMaxZ, wcsZ);
                    }

                    hasValidBody = true;
                }
                catch (Exception ex)
                {
                    // 该实体无法获取包围盒（轻量化体），跳过
                    theSession.ListingWindow.WriteLine(string.Format("        跳过实体: {0}", ex.Message));
                    continue;
                }
            }

            // 检查是否有有效实体
            if (!hasValidBody)
            {
                theSession.ListingWindow.WriteLine(string.Format("      没有可获取包围盒的实体（可能都是轻量化体）"));
                return null;
            }

            // 计算 XYZ 尺寸（WCS方向）
            double xSize = overallMaxX - overallMinX;
            double ySize = overallMaxY - overallMinY;
            double zSize = overallMaxZ - overallMinZ;

            return new double[] { xSize, ySize, zSize };
        }
        catch (Exception ex)
        {
            theSession.ListingWindow.WriteLine(string.Format("      获取WCS包围盒异常: {0}", ex.Message));
            return null;
        }
    }

    /// <summary>
    /// 将数据追加写入CSV文件
    /// </summary>
    private void WriteToCsv(string outputFolder, Component comp, double[] dimensions, Session theSession)
    {
        try
        {
            // 延迟初始化CSV文件（首次写入时创建）
            if (_csvWriter == null)
            {
                string csvPath = Path.Combine(outputFolder, CsvFileName);
                _csvWriter = new StreamWriter(csvPath, false, System.Text.Encoding.UTF8);
                _headerWritten = false;
            }

            // 写入表头（仅一次）
            if (!_headerWritten)
            {
                _csvWriter.WriteLine("零件名,ID,x,y,z");
                _headerWritten = true;
            }

            // 获取零件名称
            string partName = AssemblyTraverserUtils.GetDescriptiveName(comp);
            string partId = comp.DisplayName;

            // 写入数据行
            _csvWriter.WriteLine(string.Format("{0},{1},{2:F3},{3:F3},{4:F3}",
                partName, partId, dimensions[0], dimensions[1], dimensions[2]));
            _csvWriter.Flush();

            theSession.ListingWindow.WriteLine(string.Format(
                "      记录: {0} | {1} | X={2:F3} Y={3:F3} Z={4:F3}",
                partName, partId, dimensions[0], dimensions[1], dimensions[2]));
        }
        catch (Exception ex)
        {
            theSession.ListingWindow.WriteLine(string.Format("      CSV写入异常: {0}", ex.Message));
        }
    }

    /// <summary>
    /// 关闭CSV文件（应在遍历结束后调用）
    /// </summary>
    public void Finish()
    {
        if (_csvWriter != null)
        {
            try
            {
                _csvWriter.Close();
            }
            catch { }
            _csvWriter = null;
        }
    }
}
