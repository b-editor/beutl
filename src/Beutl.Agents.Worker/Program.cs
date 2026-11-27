using System.Text;
using Beutl.Agents;
using Beutl.Agents.Worker;

AgentWorkerProcess.IsolateOnUnix();
var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var input = new StreamReader(Console.OpenStandardInput(), encoding);
using var output = new StreamWriter(Console.OpenStandardOutput(), encoding) { NewLine = "\n" };
Console.SetOut(Console.Error);

var host = new AgentWorkerHost(input, output);
await host.RunAsync();
