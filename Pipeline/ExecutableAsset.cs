using System.Threading;
using System.Threading.Tasks;
using ACT;
using Unity.Properties;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;


namespace ModularPrompt
{
    public readonly struct ExecutionResult
    {
        public bool Succeeded { get; }
        public bool Cancelled { get; }
        public string Message { get; }
        ExecutionResult(bool succeeded, bool cancelled, string message)
        {
            Succeeded = succeeded;
            Cancelled = cancelled;
            Message = message ?? "";
        }

        public static ExecutionResult Success(string message = "") => new(true, false, message);
        public static ExecutionResult Failure(string message) => new(false, false, message);
        public static ExecutionResult Cancel(string message = "실행이 취소되었습니다.") => new(false, true, message);
    }

    public abstract class ExecutableAsset : BindableDataSource
    {
        [SerializeField] string executionId = "";

        [CreateProperty]
        public string ExecutionId
        {
            get => executionId;
            set
            {
                string next = value?.Trim() ?? "";
                if (executionId == next) return;

                Undo.RecordObject(this, "Change Execution Id");


                executionId = next;
                NotifyPropertyChanged(nameof(ExecutionId));
                MarkDirty();

                EditorUtility.SetDirty(this);
            }
        }

        [CreateProperty] public abstract string ExecutionName { get; }
        [CreateProperty] public abstract bool IsExecuting { get; }


        public abstract Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default);
        public abstract void Cancel();

        protected virtual void OnValidate()
        {
            executionId = executionId?.Trim() ?? "";
            NotifyPropertyChanged(nameof(ExecutionId));
        }

    }
}
#endif