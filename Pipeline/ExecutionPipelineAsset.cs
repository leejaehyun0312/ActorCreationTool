#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ACT;
using Unity.Properties;
using UnityEngine;

using UnityEditor;


namespace ModularPrompt
{
    public enum PipelineStatus { Idle,Running, Succeeded, Failed, Cancelling, Cancelled }

    [CreateAssetMenu(fileName = "PromptExecutionPipeline", menuName = "ExecutionPipeline/Prompt Execution Pipeline Asset")]
    public sealed class ExecutionPipelineAsset : BindableDataSource
    {
        [SerializeField] List<ExecutableAsset> executions = new();
        [SerializeField] bool openNextPageOnSucceeded = true;

        [NonSerialized] CancellationTokenSource cancellationSource;
        [NonSerialized] int executionGate;

        PipelineStatus status = PipelineStatus.Idle;

        int currentExecutionIndex = -1;
        string currentExecutionName = "";
        string currentPageId = "";
        string message = "";

        [CreateProperty] public List<ExecutableAsset> Executions => executions;
        [CreateProperty] public bool IsExecuting => Volatile.Read(ref executionGate) == 1;

        [CreateProperty]
        public bool OpenNextPageOnSucceeded
        {
            get => openNextPageOnSucceeded;
            set => SetProperty(ref openNextPageOnSucceeded, value);
        }

        [CreateProperty]
        public PipelineStatus Status
        {
            get => status;
            private set => SetProperty(ref status, value);
        }

        [CreateProperty]
        public int CurrentExecutionIndex
        {
            get => currentExecutionIndex;
            private set => SetProperty(ref currentExecutionIndex, value);
        }

        [CreateProperty]
        public string CurrentExecutionName
        {
            get => currentExecutionName;
            private set => SetProperty(ref currentExecutionName, value ?? "");
        }

        [CreateProperty]
        public string CurrentPageId
        {
            get => currentPageId;
            private set => SetProperty(ref currentPageId, value ?? "");
        }

        [CreateProperty]
        public string Message
        {
            get => message;
            private set => SetProperty(ref message, value ?? "");
        }

        [CreateProperty]
        public string StatusText => Status switch
        {
            PipelineStatus.Idle => "진행 가능",
            PipelineStatus.Running => string.IsNullOrWhiteSpace(CurrentExecutionName) ? "실행 중" : $"{CurrentExecutionName} 실행 중",
            PipelineStatus.Succeeded => "전체 실행 완료",
            PipelineStatus.Failed => "실행 실패",
            PipelineStatus.Cancelling => "실행 취소 중",
            PipelineStatus.Cancelled => "실행 취소",
            _ => "-"
        };

        public async void ExecutePipeline() => await ExecutePipelineAsync();

        public async Task<ExecutionResult> ExecutePipelineAsync()
        {
            if (Interlocked.CompareExchange(ref executionGate, 1, 0) != 0) return ExecutionResult.Failure("파이프라인이 이미 실행 중입니다.");

            cancellationSource?.Dispose();
            cancellationSource = new CancellationTokenSource();
            SetPipelineState(PipelineStatus.Running, -1, "", "", "현재 페이지를 확인하고 있습니다.");

            try
            {
                if (!BlueprintWizardWindow.TryGetCurrentPageId(out string pageId, out string pageError))  return FailPipeline(pageError);

                pageId = pageId?.Trim() ?? "";

                if (!TryFindExecution(pageId, out _, out int startIndex, out string executionError))return FailPipeline(executionError);

                for (int i = startIndex; i < executions.Count; i++)
                {
                    cancellationSource.Token.ThrowIfCancellationRequested();

                    ExecutableAsset execution = executions[i];

                    if (execution == null) return FailPipeline($"{i + 1}번째 실행기가 비어 있습니다.");

                    string executionId = execution.ExecutionId?.Trim() ?? "";

                    if (string.IsNullOrWhiteSpace(executionId)) return FailPipeline($"{execution.ExecutionName}의 Execution ID가 비어 있습니다.");

                    if (i > startIndex && OpenNextPageOnSucceeded)
                    {
                        if (!BlueprintWizardWindow.TryOpenNextPage(out string nextPageError)) return FailPipeline(nextPageError);

                        if (!BlueprintWizardWindow.TryGetCurrentPageId(out pageId, out pageError))return FailPipeline(pageError);

                        pageId = pageId?.Trim() ?? "";

                        if (!string.Equals(pageId, executionId, StringComparison.OrdinalIgnoreCase))
                            return FailPipeline($"페이지와 실행기 순서가 일치하지 않습니다.\n현재 페이지: {pageId}\n실행기 ID: {executionId}");
                    }
                    else
                    {
                        pageId = executionId;
                    }

                    SetPipelineState(PipelineStatus.Running, i, execution.ExecutionName, pageId, $"{execution.ExecutionName} 실행 중");

                    ExecutionResult result = await execution.ExecuteAsync(cancellationSource.Token);

                    if (result.Cancelled)
                    {
                        string cancelMessage = string.IsNullOrWhiteSpace(result.Message) ? "실행이 취소되었습니다." : result.Message;
                        SetPipelineState(PipelineStatus.Cancelled, i, execution.ExecutionName, pageId, cancelMessage);
                        return ExecutionResult.Cancel(cancelMessage);
                    }

                    if (!result.Succeeded)
                    {
                        string failureMessage = string.IsNullOrWhiteSpace(result.Message) ? $"{execution.ExecutionName} 실행에 실패했습니다." : result.Message;

                        return FailPipeline(failureMessage);
                    }

                    Message = string.IsNullOrWhiteSpace(result.Message) ? $"{execution.ExecutionName} 실행이 완료되었습니다."  : result.Message;

                    NotifyPipelineChanged();
                }

                SetPipelineState(PipelineStatus.Succeeded, executions.Count - 1, CurrentExecutionName, CurrentPageId, "모든 실행이 완료되었습니다.");
                return ExecutionResult.Success(Message);
            }
            catch (OperationCanceledException)
            {
                SetPipelineState(PipelineStatus.Cancelled, CurrentExecutionIndex, CurrentExecutionName, CurrentPageId, "실행이 취소되었습니다.");
                return ExecutionResult.Cancel(Message);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return FailPipeline(exception.Message);
            }
            finally
            {
                cancellationSource?.Dispose();
                cancellationSource = null;
                Interlocked.Exchange(ref executionGate, 0);
                NotifyPipelineChanged();
                EditorUtility.SetDirty(this);
            }
        }

        public void CancelPipeline()
        {
            if (!IsExecuting) return;

            Status = PipelineStatus.Cancelling;
            Message = "현재 실행기를 취소하고 있습니다.";
            NotifyPipelineChanged();

            if (CurrentExecutionIndex >= 0 && CurrentExecutionIndex < executions.Count)  executions[CurrentExecutionIndex]?.Cancel();

            cancellationSource?.Cancel();
        }

        public void ResetPipeline()
        {
            if (IsExecuting) return;
            SetPipelineState(PipelineStatus.Idle, -1, "", "", "");
        }

        bool TryFindExecution(string pageId, out ExecutableAsset execution, out int executionIndex, out string error)
        {
            execution = null;
            executionIndex = -1;
            error = "";
            pageId = pageId?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(pageId))
            {
                error = "현재 페이지 ID가 비어 있습니다.";
                return false;
            }

            for (int i = 0; i < executions.Count; i++)
            {
                ExecutableAsset candidate = executions[i];
                if (candidate == null) continue;

                string executionId = candidate.ExecutionId?.Trim() ?? "";
                if (!string.Equals(executionId, pageId, StringComparison.OrdinalIgnoreCase)) continue;

                if (execution != null)
                {
                    error = $"같은 페이지 ID를 사용하는 실행기가 여러 개 있습니다: {pageId}";
                    return false;
                }

                execution = candidate;
                executionIndex = i;
            }

            if (execution != null) return true;

            List<string> registeredIds = new();

            foreach (ExecutableAsset candidate in executions)
            {
                if (candidate == null) continue;
                registeredIds.Add(string.IsNullOrWhiteSpace(candidate.ExecutionId) ? "(비어 있음)" : candidate.ExecutionId);
            }

            error = registeredIds.Count == 0
                ? $"현재 페이지와 연결된 실행기를 찾을 수 없습니다: {pageId}\n등록된 실행기가 없습니다."
                : $"현재 페이지와 연결된 실행기를 찾을 수 없습니다: {pageId}\n등록된 실행 ID: {string.Join(", ", registeredIds)}";

            return false;
        }

        ExecutionResult FailPipeline(string failureMessage)
        {
            failureMessage = string.IsNullOrWhiteSpace(failureMessage) ? "알 수 없는 이유로 실행에 실패했습니다." : failureMessage;
            Debug.LogError($"[ExecutionPipeline] {failureMessage}", this);
            SetPipelineState(PipelineStatus.Failed, CurrentExecutionIndex, CurrentExecutionName, CurrentPageId, failureMessage);
            return ExecutionResult.Failure(failureMessage);
        }

        void SetPipelineState(PipelineStatus nextStatus, int nextIndex, string nextExecutionName, string nextPageId, string nextMessage)
        {
            Status = nextStatus;
            CurrentExecutionIndex = nextIndex;
            CurrentExecutionName = nextExecutionName;
            CurrentPageId = nextPageId;
            Message = nextMessage;
            NotifyPipelineChanged();
        }

        void NotifyPipelineChanged()
        {
            NotifyPropertyChanged(nameof(IsExecuting));
            NotifyPropertyChanged(nameof(Status));
            NotifyPropertyChanged(nameof(CurrentExecutionIndex));
            NotifyPropertyChanged(nameof(CurrentExecutionName));
            NotifyPropertyChanged(nameof(CurrentPageId));
            NotifyPropertyChanged(nameof(Message));
            NotifyPropertyChanged(nameof(StatusText));
            MarkDirty();
        }
    }
}
#endif