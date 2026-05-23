// 单线程协程工具类:可选委托/条件字段按设计可为 null,整体退出可空注解上下文。
#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Common
{
    /// <summary>
    /// 单线程模拟异步执行器 - 最终修正版，解决了在迭代时修改集合的问题。
    /// </summary>
    public class AsyncSimulator
    {
        #region 字段

        // 主任务列表
        private static readonly List<SimulatedTask> _tasks = new List<SimulatedTask>();
        // 已完成任务列表，用于调试
        private static readonly List<SimulatedTask> _completedTasks = new List<SimulatedTask>();
        // 【关键】用于缓冲在Update循环中新创建的任务，以避免并发修改异常
        private static readonly List<SimulatedTask> _pendingTasks = new List<SimulatedTask>();

        private static float _currentTime = 0f;
        private static int _nextTaskId = 1;

        #endregion

        #region 属性

        /// <summary>
        /// 获取模拟器当前的时间
        /// </summary>
        public static float CurrentTime => _currentTime;

        #endregion

        #region 嵌套类

        /// <summary>
        /// 模拟异步任务的内部表示
        /// </summary>
        public class SimulatedTask
        {
            public int Id { get; set; }
            public float StartTime { get; set; }
            public float DelayTime { get; set; }
            public Action Action { get; set; }
            public bool IsCompleted { get; set; }
            public bool IsCancelled { get; set; }
            public Func<bool> Condition { get; set; }
            public Stack<IEnumerator> CoroutineStack { get; set; }

            public bool ShouldExecute()
            {
                if (IsCancelled || IsCompleted)
                    return false;

                if (CoroutineStack != null && CoroutineStack.Count > 0)
                    return true;

                if (Condition != null)
                {
                    bool isTimeout = DelayTime > 0f && _currentTime >= StartTime + DelayTime;
                    return Condition() || isTimeout;
                }

                return _currentTime >= StartTime + DelayTime;
            }
        }

        #endregion

        #region 核心更新逻辑

        /// <summary>
        /// 更新时间并执行到期的任务。这是模拟器的核心，应在游戏主循环中每帧调用。
        /// </summary>
        public static void Update(float deltaTime)
        {
            _currentTime += deltaTime;

            // 阶段1: 将上一帧挂起的任务合并到主任务列表
            if (_pendingTasks.Count > 0)
            {
                _tasks.AddRange(_pendingTasks);
                _pendingTasks.Clear();
            }

            // 阶段2: 遍历并执行所有可执行的任务。
            // 使用常规 for 循环以避免在 foreach 中修改集合的问题。
            for (int i = 0; i < _tasks.Count; i++)
            {
                var task = _tasks[i];
                if (task.IsCompleted || task.IsCancelled) continue;

                if (task.ShouldExecute())
                {
                    try
                    {
                        if (task.CoroutineStack != null && task.CoroutineStack.Count > 0)
                        {
                            ExecuteCoroutineStep(task);
                        }
                        else
                        {
                            task.Action?.Invoke();
                            task.IsCompleted = true; // 标记普通Action任务为完成
                        }
                    }
                    catch (Exception ex)
                    {
                        // 统一处理所有任务执行中发生的异常
                        if (task.CoroutineStack != null && task.CoroutineStack.Count > 0)
                        {
                            LogErrorWithCoroutineStack(task, ex);
                        }
                        else
                        {
                            Logger.Error($"[AsyncSimulator] Action Task {task.Id} failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                        }
                        // 将出错的任务标记为取消和完成，以便在下一阶段被移除
                        task.IsCancelled = true;
                        task.IsCompleted = true;
                    }
                }
            }

            // 阶段3: 安全地清理所有已完成或已取消的任务。
            _tasks.RemoveAll(task =>
            {
                if (task.IsCompleted || task.IsCancelled)
                {
                    if (task.IsCompleted && !task.IsCancelled)
                    {
                        _completedTasks.Add(task);
                    }
                    return true;
                }
                return false;
            });

            // 清理旧的已完成任务记录，防止内存无限增长
            if (_completedTasks.Count > 100)
            {
                _completedTasks.RemoveRange(0, _completedTasks.Count - 100);
            }
        }

        /// <summary>
        /// 执行单步协程逻辑
        /// </summary>
        private static void ExecuteCoroutineStep(SimulatedTask task)
        {
            IEnumerator currentCoroutine = task.CoroutineStack.Peek();

            if (!currentCoroutine.MoveNext())
            {
                task.CoroutineStack.Pop();
                if (task.CoroutineStack.Count == 0)
                {
                    task.IsCompleted = true;
                }
            }
            else
            {
                object yieldedValue = currentCoroutine.Current;
                if (yieldedValue is IEnumerator newCoroutine)
                {
                    task.CoroutineStack.Push(newCoroutine);
                }
                else if (yieldedValue is SimulatedTask yieldedTask)
                {
                    task.CoroutineStack.Push(WaitForTask(yieldedTask));
                }
            }
        }

        #endregion

        #region 任务创建方法

        public static SimulatedTask StartCoroutine(IEnumerator coroutine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(coroutine);

            var task = new SimulatedTask
            {
                Id = _nextTaskId++,
                StartTime = _currentTime,
                CoroutineStack = stack
            };

            _pendingTasks.Add(task);
            return task;
        }

        public static int Delay(float delaySeconds, Action action)
        {
            var task = new SimulatedTask
            {
                Id = _nextTaskId++,
                StartTime = _currentTime,
                DelayTime = delaySeconds,
                Action = action
            };

            _pendingTasks.Add(task);
            return task.Id;
        }

        public static int WaitUntil(Func<bool> condition, Action action, float timeoutSeconds = 0f)
        {
            var task = new SimulatedTask
            {
                Id = _nextTaskId++,
                StartTime = _currentTime,
                DelayTime = timeoutSeconds,
                Condition = condition,
                Action = action
            };

            _pendingTasks.Add(task);
            return task.Id;
        }

        public static int Repeat(float intervalSeconds, Action action, Func<bool> stopCondition = null, int maxExecutions = 0, Action onComplete = null)
        {
            int taskId = _nextTaskId++;
            int executionCount = 0;
            Action repeatAction = null;

            repeatAction = () =>
            {
                if ((stopCondition != null && stopCondition()) || (maxExecutions > 0 && executionCount >= maxExecutions))
                {
                    onComplete?.Invoke();
                    return;
                }

                action?.Invoke();
                executionCount++;

                if (!((stopCondition != null && stopCondition()) || (maxExecutions > 0 && executionCount >= maxExecutions)))
                {
                    var nextTask = new SimulatedTask
                    {
                        Id = taskId, // 共享ID以便统一取消
                        StartTime = _currentTime,
                        DelayTime = intervalSeconds,
                        Action = repeatAction
                    };
                    _pendingTasks.Add(nextTask);
                }
                else
                {
                    onComplete?.Invoke();
                }
            };

            var initialTask = new SimulatedTask
            {
                Id = taskId,
                StartTime = _currentTime,
                DelayTime = 0f,
                Action = repeatAction
            };
            _pendingTasks.Add(initialTask);
            return taskId;
        }

        #endregion

        #region 任务管理

        public static bool CancelTask(int taskId)
        {
            bool cancelled = false;
            foreach (var task in _tasks)
            {
                if (task.Id == taskId)
                {
                    task.IsCancelled = true;
                    cancelled = true;
                }
            }
            foreach (var task in _pendingTasks)
            {
                if (task.Id == taskId)
                {
                    task.IsCancelled = true;
                    cancelled = true;
                }
            }
            return cancelled;
        }

        public static bool CancelTask(SimulatedTask taskToCancel)
        {
            if (taskToCancel == null) return false;
            taskToCancel.IsCancelled = true;
            return true;
        }

        public static void CancelAllTasks()
        {
            foreach (var task in _tasks)
            {
                task.IsCancelled = true;
            }
            foreach (var task in _pendingTasks)
            {
                task.IsCancelled = true;
            }
        }

        public static void Reset()
        {
            _tasks.Clear();
            _completedTasks.Clear();
            _pendingTasks.Clear();
            _currentTime = 0f;
            _nextTaskId = 1;
        }

        #endregion

        #region 私有辅助和日志

        private static IEnumerator WaitForTask(SimulatedTask task)
        {
            while (!task.IsCompleted && !task.IsCancelled)
            {
                yield return null;
            }
        }

        private static void LogErrorWithCoroutineStack(SimulatedTask task, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[AsyncSimulator] Coroutine task {task.Id} failed!");
            sb.AppendLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine("  Coroutine Call Stack:");

            var stackArray = task.CoroutineStack.ToArray();
            for (int j = stackArray.Length - 1; j >= 0; j--)
            {
                var coroutine = stackArray[j];
                string fullTypeName = coroutine.GetType().FullName;
                string methodName = "UnknownMethod";

                if (fullTypeName.Contains("<") && fullTypeName.Contains(">"))
                {
                    try
                    {
                        methodName = fullTypeName.Substring(fullTypeName.IndexOf('<') + 1, fullTypeName.IndexOf('>') - fullTypeName.IndexOf('<') - 1);
                    }
                    catch { /* 解析失败则使用默认值 */ }
                }

                sb.Append($"    at {methodName}");
                if (j > 0)
                {
                    sb.Append(" -> yield return");
                }
                sb.AppendLine();
            }

            sb.AppendLine("  C# Stack Trace:");
            sb.AppendLine(ex.StackTrace);

            Logger.Error(sb.ToString());
        }

        #endregion

        #region 状态查询

        public static int GetActiveTaskCount() => _tasks.Count + _pendingTasks.Count;

        public static string GetTaskStatus() => $"Active: {_tasks.Count}, Pending: {_pendingTasks.Count}, Completed: {_completedTasks.Count}, Current Time: {_currentTime:F2}s";

        #endregion
    }

    /// <summary>
    /// 协程帮助类 - 提供常用的协程返回值
    /// </summary>
    public static class CoroutineHelper
    {
        public static IEnumerator WaitForSeconds(float seconds)
        {
            float endTime = AsyncSimulator.CurrentTime + seconds;
            while (AsyncSimulator.CurrentTime < endTime)
            {
                yield return null;
            }
        }

        public static IEnumerator WaitForNextFrame()
        {
            yield return null;
        }

        public static IEnumerator WaitUntilCondition(Func<bool> condition)
        {
            while (!condition())
            {
                yield return null;
            }
        }
    }
}
