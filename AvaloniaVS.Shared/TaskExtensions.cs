using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.Internal.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Serilog;

namespace AvaloniaVS
{
    internal static class TaskExtensions
    {
        public static void FireAndForget(this Task task)
        {
            _ = task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Log.Error(t.Exception, "Exception caught by FireAndForget");
                }
            }, TaskScheduler.Default);
        }


        public static IVsTask AsVsTask(this JoinableTask joinableTask)
        {
            JoinableTask joinableTask2 = Requires.NotNull(joinableTask, "joinableTask");
            IVsTaskCompletionSource vsTaskCompletionSource = MpfHelpers.VsTaskSchedulerService.CreateTaskCompletionSource();
            vsTaskCompletionSource.CompleteAfterTask(joinableTask.Task);
            IVsTaskJoinableTask vsTaskJoinableTask = (IVsTaskJoinableTask)vsTaskCompletionSource.Task;
            vsTaskJoinableTask.AssociateJoinableTask(joinableTask);
            bool promoted = false;
            IVsTaskEvents taskEvents = (IVsTaskEvents)vsTaskCompletionSource.Task;
            CancellationTokenSource blockingCancellationSource = new CancellationTokenSource();
            EventHandler unblockDelegate = null;
            unblockDelegate = delegate (object sender, EventArgs e)
            {
                blockingCancellationSource.Cancel();
                blockingCancellationSource = new CancellationTokenSource();
                if (sender is IVsTaskEvents vsTaskEvents2)
                {
                    vsTaskEvents2.OnBlockingWaitEnd -= unblockDelegate;
                }
            };
            EventHandler<BlockingTaskEventArgs> markedAsBlocking = delegate (object sender, BlockingTaskEventArgs e)
            {
                if (!promoted)
                {
                    promoted = true;
                    Action action = delegate
                    {
                        if (e.BlockedTask is IVsTaskEvents vsTaskEvents)
                        {
                            vsTaskEvents.OnBlockingWaitEnd += unblockDelegate;
                        }

                        try
                        {
                            joinableTask.Join(blockingCancellationSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch
                        {
                        }
                    };
                    if (ThreadHelper.CheckAccess())
                    {
                        action();
                    }
                    else
                    {
                        UIThreadReentrancyScope.EnqueueActionAsync(action).Forget();
                    }
                }
            };
            taskEvents.OnMarkedAsBlocking += markedAsBlocking;
            IVsTask vsTask = vsTaskCompletionSource.Task.ContinueWith(VsTaskRunContext.BackgroundThread, VsTaskLibraryHelper.CreateTaskBody(delegate
            {
                taskEvents.OnMarkedAsBlocking -= markedAsBlocking;
                taskEvents.OnBlockingWaitEnd -= unblockDelegate;
            }));
            return vsTaskCompletionSource.Task;
        }


        public static void CompleteAfterTask(this IVsTaskCompletionSource taskCompletionSource, Task task)
        {
            Requires.NotNull(task, "task");
            Requires.NotNull(taskCompletionSource, "taskCompletionSource");
            if (!CopyTaskResultIfCompleted(task, taskCompletionSource))
            {
                task.ContinueWith((Task _, object source) =>
                    CopyTaskResultIfCompleted(task, (IVsTaskCompletionSource)source), taskCompletionSource,
                            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).FireAndForget();
            }
        }


        //
        // Summary:
        //     Returns true if the task was completed, otherwise false.
        private static bool CopyTaskResultIfCompleted(Task task, IVsTaskCompletionSource taskCompletionSource)
        {
            if (task.IsCanceled)
            {
                taskCompletionSource.SetCanceled();
            }
            else if (task.IsFaulted)
            {
                taskCompletionSource.SetFaulted(Marshal.GetHRForException(task.Exception.InnerException ?? task.Exception));
            }
            else
            {
                if (!task.IsCompleted)
                {
                    return false;
                }

                taskCompletionSource.SetResult(true);
            }

            return true;
        }
    }
}
