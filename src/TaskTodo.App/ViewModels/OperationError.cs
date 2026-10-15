using TaskTodo.Core.Services;

namespace TaskTodo.App.ViewModels;

public static class OperationError
{
    public static string Describe(TaskTodoException error) => error.Code switch
    {
        TaskTodoErrorCode.Validation => "输入不能为空；任务标题最多 500 个字符，清单名称最多 200 个字符。请修改后重试。",
        TaskTodoErrorCode.StorageAccessDenied => "无法写入数据目录。请检查目录权限后重试。",
        TaskTodoErrorCode.StorageUnavailable => "保存失败，数据库可能正被占用或磁盘不可用。请重试。",
        TaskTodoErrorCode.DatabaseCorrupt or TaskTodoErrorCode.InvalidStoredData
            => "无法读取已有数据，原文件已保留。请检查数据目录或从备份恢复。",
        TaskTodoErrorCode.UnsupportedDatabaseVersion => "此数据库由更新版本创建，请使用相应版本打开。",
        TaskTodoErrorCode.BackupFailed => "备份未能完成，原数据未被替换。请检查备份目录权限和磁盘空间后重试。",
        TaskTodoErrorCode.NotFound or TaskTodoErrorCode.Conflict => "任务数据已变化，本次操作未保存。请重新打开后再试。",
        _ => "操作未完成，请重试。错误类型：" + error.Code,
    };
}
