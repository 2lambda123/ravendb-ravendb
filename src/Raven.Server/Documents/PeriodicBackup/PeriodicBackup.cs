using System;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Util;
using Raven.Server.ServerWide;
using Sparrow.Collections;
using Sparrow.Threading;

namespace Raven.Server.Documents.PeriodicBackup
{
    public class PeriodicBackup : IDisposable
    {
        private readonly SemaphoreSlim _updateBackupTaskSemaphore = new SemaphoreSlim(1);

        private readonly DisposeOnce<SingleAttempt> _disposeOnce;

        public Timer BackupTimer { get; private set; }

        public Task RunningTask { get; set; }

        public long? RunningBackupTaskId { get; set; }

        public OperationCancelToken CancelToken { get; set; }

        public DateTime StartTimeInUtc { get; set; }

        public PeriodicBackupConfiguration Configuration { get; set; }

        public PeriodicBackupStatus BackupStatus { get; set; }

        public PeriodicBackupStatus RunningBackupStatus { get; set; }

        public bool Disposed => _disposeOnce.Disposed;

        public PeriodicBackup(ConcurrentSet<Task> inactiveRunningPeriodicBackupsTasks)
        {
            _disposeOnce = new DisposeOnce<SingleAttempt>(() =>
            {
                using (UpdateBackupTask())
                {
                    CancelFutureTasks();

                    if (RunningTask?.IsCompleted == false)
                    {
                        inactiveRunningPeriodicBackupsTasks.Add(RunningTask);
                    }
                }
            });
        }

        public IDisposable UpdateBackupTask()
        {
            _updateBackupTaskSemaphore.Wait();

            return new DisposableAction(() => _updateBackupTaskSemaphore.Release());
        }

        public void DisableFutureBackups()
        {
            using (UpdateBackupTask())
            {
                CancelFutureTasks();
            }
        }

        private void CancelFutureTasks()
        {
            BackupTimer?.Dispose();
            BackupTimer = null;

            try
            {
                CancelToken?.Cancel();
            }
            catch
            {
            }
        }

        public void UpdateTimer(Timer newBackupTimer, bool discardIfDisabled = false)
        {
            using (UpdateBackupTask())
            {
                if (Disposed)
                {
                    newBackupTimer.Dispose();
                    return;
                }

                if (discardIfDisabled && BackupTimer == null)
                {
                    newBackupTimer.Dispose();
                    return;
                }

                BackupTimer?.Dispose();
                BackupTimer = newBackupTimer;
            }
        }

        public bool HasScheduledBackup()
        {
            return BackupTimer != null;
        }

        public void Dispose()
        {
            _disposeOnce.Dispose();
        }
    }
}
