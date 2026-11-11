# 可选日志接入 API v1

命名空间：`ModConfigEnhance.Diagnostics`；`MceLogApi.ApiVersion = 1`。
MCE 不需要联网，不自动导出或上传日志。自定义来源在“日志 → 自定义日志”查看并单独导出。

## 三种来源

```csharp
// 已有文件：绝对路径。只登记和读取，不执行该文件。
IDisposable registration = MceLogApi.RegisterFile(pluginGuid, "network", "网络诊断", path);

// 已有内存日志：后台调用，只能快速复制已有数据，不允许调用 Unity。
IDisposable registration = MceLogApi.RegisterSnapshot(pluginGuid, "memory", "运行诊断", CopyExistingRecords);

// 新日志通道：线程安全、有界；调用方在自己的主线程采集 Unity 状态。
MceLogChannel channel = MceLogApi.CreateChannel(pluginGuid, "runtime", "运行日志", 2000);
channel.Write(new MceLogRecord {
    Level = MceLogLevel.Warning, Category = "Network", Message = "连接中断",
    SubjectId = connectionId, SubjectName = connectionName
});
channel.Clear(); // 自动产生新 Epoch，序号从 1 重新开始。
channel.Dispose(); // 卸载时注销。
```

唯一来源身份为 `pluginGuid/sourceId`，重复注册会报错。最多 128 来源，每个来源查看最多 10000 条；通道容量 1～10000，单条正文上限 256KiB。生产者应自行限制调用栈、附加字段和快照大小。来源拥有者负责实际文件路径的可信性。

快照记录须使用稳定 `Epoch + Sequence`。清空/重启日志后更换 Epoch；不要每次读取重新编号。快照必须返回快速复制的已有日志，不做网络、磁盘遍历或 Unity 状态采集。抛错隔离在该来源，阻塞提供器仍会占用一个后台任务，不在游戏线程强制等待。

## 无编译依赖的文本快照桥接

已有控制台可以通过反射调用公开方法：

```csharp
var assembly = AppDomain.CurrentDomain.GetAssemblies()
    .FirstOrDefault(a => a.GetName().Name == "ModConfigEnhance");
var api = assembly?.GetType("ModConfigEnhance.Diagnostics.MceLogApi");
var method = api?.GetMethod("RegisterTextSnapshot");
registration = method?.Invoke(null, new object[] {
    pluginGuid, "diagnostic", "我的模组 · 诊断",
    new Func<IReadOnlyDictionary<long, string>>(CopyNumberedLines),
    new Func<string>(GetEpoch)
}) as IDisposable;
```

字典键为稳定递增序号，值为原文；建议格式 `[HH:mm:ss] [Warning:分类] 正文`。回调可能在后台执行，使用锁复制已有数据，不能读取 Unity 对象。MCE 未安装时继续使用原控制台，可低频重试注册，成功后保留句柄，销毁时 Dispose。不要复制 MCE DLL 到自己的发行目录。

真实接入样例：`MODs/PeakVoiceFixFix/PeakVoiceFix/VoiceUIManager.cs`，原有约 300 条诊断记录镜像为线程安全快照，不触发原模组磁盘导出，清空时更换代次；MCE 缺席不影响语音功能。

## 导出与隐私

自定义导出只包含所选来源；元信息复选框默认开启，可取消。原日志正文保留原文，包括玩家名称和生产者写入的其他内容。元信息中的密码、令牌、密钥字段进行遮盖，用户目录路径替换占位符。接入方应避免向日志正文写入凭证。
