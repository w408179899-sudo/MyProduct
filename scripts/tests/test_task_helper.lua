-- 辅助脚本: 供 test_task.lua 的文件路径任务测试使用
-- 设置进度并通过共享变量通知测试完成
task.set_progress(0.5)
sys.sleep(50)
task.set_progress(1.0)
sys.set_share("_test_file_task_done", 1)
