**Azure function durable orchestrator to test the load and elastic plans.**

This function consist of 1 durable orchestrator, that call 3 times the same activity.  
This activity will perform 1.000.000 operantions and wait

- **To start some orchestrator:**
`curl -X GET "http://localhost:7126/api/Function_HttpStart?instancecount=10"`  
Enter the amount of orchestrator you want in `instancecount`. It will start the amount of orchestrator by creating instanceIds starting by 0.
If an instance with the same id exists already and is running, it will skip this one.

- **To stop some orchestrator:**
`curl -X GET "http://localhost:7126/api/Function_HttpStop?instancecount=10"`  
It wil stop all orchestrator with their id starting with 0 to the amount asked. 
If you have only one orchestrator running, but with the id = 2, you need to ask to stop at least 3 orchestrator to reach the id 2.


- **Query the orchestrator status:**
`curl -X GET "http://localhost:7126/runtime/webhooks/durabletask/instances?connection=Storage"`
You can use the Azure orchestrator API: https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-http-api


