using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace TestAzureOrchestratorLoad
{
    public class FunctionStatus
    {
        public string? TaskName { get; set; }
        public string? Message { get; set; }
        public DateTime? NextExecution { get; set; }
    }

    public class Function
    {
        private static string FonctionIdPrefix = "FunctionTest";
        private static int NextExecutionDelay_sec = 10;

        [Function(nameof(RunOrchestrator))]
        public async Task RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            ILogger logger = context.CreateReplaySafeLogger(nameof(Function));

            var input = context.GetInput<string>();
            logger.LogInformation($"Start orchestrator {input}");

            // Create the task status
            FunctionStatus status = new()
            {
                TaskName = input,
                Message = "Executing Stage 1"
            };
            context.SetCustomStatus(status);
            await context.CallActivityAsync<string>(nameof(StartActivity), "Stage 1");

            status.Message = "Executing Stage 2";
            context.SetCustomStatus(status);
            await context.CallActivityAsync<string>(nameof(StartActivity), "Stage 2");

            status.Message = "Executing Stage 3";
            context.SetCustomStatus(status);
            await context.CallActivityAsync<string>(nameof(StartActivity), "Stage 3");

            // Wait stop event or delay to restart
            DateTime nextTask = context.CurrentUtcDateTime.AddSeconds(NextExecutionDelay_sec);
            status.Message = "Waiting stop or next execution";
            status.NextExecution = nextTask;
            context.SetCustomStatus(status);
            using (var cts = new CancellationTokenSource())
            {
                Task sleepingTask = context.CreateTimer(nextTask, cts.Token);
                Task timeoutTask = context.WaitForExternalEvent<string>("stop");

                Task winner = await Task.WhenAny(sleepingTask, timeoutTask);
                if (winner == sleepingTask)
                {
                    // can continue
                }
                else
                {
                    logger.LogInformation($"stop recieved for instance {input}");
                    cts.Cancel();
                    status.Message = "Operation cancelled while sleeping";
                    status.NextExecution = null;
                    context.SetCustomStatus(status);

                    // Stop the watchdog
                    return;
                }
            }

            // Restart the Orchestrator
            context.ContinueAsNew(input);
        }

        [Function(nameof(StartActivity))]
        public async Task StartActivity([ActivityTrigger] string name, FunctionContext executionContext)
        {
            ILogger logger = executionContext.GetLogger($"Activity {name}");

            // Run a task to simulate hard work and async calls
            await Task.Run(async () =>
            {
                int result = 0;
                for (int i = 0; i < 1000000; i++)
                {
                    result += i;
                }
                await Task.Delay(1000);
            });
        }

        [Function(nameof(HttpStart))]
        public async Task<IActionResult> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest request,
            [DurableClient] DurableTaskClient client,
            FunctionContext executionContext)
        {
            ILogger logger = executionContext.GetLogger("Function_HttpStart");

            string countstr = request.Query["instancecount"];
            if (string.IsNullOrEmpty(countstr))
                return new BadRequestObjectResult("No InstanceCount");

            if (!int.TryParse(countstr, out int count))
                return new BadRequestObjectResult("InstanceCount should be int");

            int instanceStarted = 0;
            List<int> instanceIds = [];
            for (int i = 0; i < count; i++)
                instanceIds.Add(i);

            await Parallel.ForEachAsync(instanceIds, async (id, ct) =>
            {
                var instanceId = $"{FonctionIdPrefix}:{id}";
                var existingInstance = await client.GetInstancesAsync(instanceId);

                if (existingInstance == null
                || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Completed
                || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Failed
                || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
                {
                    await client.ScheduleNewOrchestrationInstanceAsync(nameof(RunOrchestrator), $"{id}", new StartOrchestrationOptions(instanceId), ct);
                    logger.LogInformation("Started orchestration with ID = '{instanceId}'.", instanceId);
                    instanceStarted++;
                }
            });

            if (instanceStarted == 0)
                return new BadRequestObjectResult("No watchdogs started");

            return new OkObjectResult($"{instanceStarted} instances started ({count} requested)");
        }

        [Function(nameof(HttpStop))]
        public async Task<IActionResult> HttpStop(
           [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest request,
           [DurableClient] DurableTaskClient client,
           FunctionContext executionContext)
        {
            ILogger logger = executionContext.GetLogger("Function_HttpStop");

            string countstr = request.Query["instancecount"];
            if (string.IsNullOrEmpty(countstr))
                return new BadRequestObjectResult("No InstanceCount");

            if (!int.TryParse(countstr, out int count))
                return new BadRequestObjectResult("InstanceCount should be int");

            int instanceStopped = 0;
            List<int> instanceIds = [];
            for (int i = 0; i < count; i++)
                instanceIds.Add(i);

            await Parallel.ForEachAsync(instanceIds, async (id, ct) =>
            {
                var instanceId = $"{FonctionIdPrefix}:{id}";
                var existingInstance = await client.GetInstancesAsync(instanceId);

                if (existingInstance != null
                && (existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Running
                || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Pending
                || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Suspended))
                {
                    await client.RaiseEventAsync(instanceId, "stop", cancellation: ct);
                    instanceStopped++;
                }
            });
            if (instanceStopped == 0)
                return new BadRequestObjectResult("No watchdogs stopped");

            return new OkObjectResult($"{instanceStopped} instances stopped ({count} requested)");
        }

    }
}
