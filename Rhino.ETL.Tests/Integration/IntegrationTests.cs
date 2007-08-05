using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MbUnit.Framework;
using Microsoft.VisualStudio.WebHost;

namespace Rhino.ETL.Tests.Integration
{
	[TestFixture]
	public class IntegrationTests : IntegrationTestBase
	{
		[Test]
		public void WillCopyRowsFromOneTableToAnother()
		{
			ExecutePackage("basic");
			AssertRowNotZero();
		}

		[Test]
		public void WillSkipSingleRowWithBadEmail()
		{
			ExecutePackage("basic");
			AssertRowCount(8);
		}


		[Test]
		public void CopyUsersWithJoin()
		{
			EtlConfigurationContext configurationContext = BuildContext(@"Joins\join_two_tables.retl");
			ExecutionPackage package = configurationContext.BuildPackage();
			package.Execute("default");

			AssertRowCount(4);
		}

		[Test]
		public void When_NOT_UsingTransaction_AndExceptionThrown_WillKeepCompletedOperation()
		{
			EtlConfigurationContext context = BuildContext(@"Errors\target_with_throwing_component.retl");

			ExecutionPackage package = context.BuildPackage();
			ExecutionResult result = package.Execute("withOutTransaction");
			Assert.AreEqual(ExecutionStatus.Failure, result.Status);

			AssertRowCount(8);
		}

		[Test]
		public void WhenUsingTransaction_AndExceptionThrown_WillRollBack()
		{
			EtlConfigurationContext context = BuildContext(@"Errors\target_with_throwing_component.retl");

			ExecutionPackage package = context.BuildPackage();
			ExecutionResult result = package.Execute("withTransaction");
			Assert.AreEqual(ExecutionStatus.Failure, result.Status);

			AssertRowCount(0);
		}

		[Test]
		public void WhenUsingTransaction_WithExplicitIsolationLevel_AndExceptionThrown_WillRollBack()
		{
			EtlConfigurationContext context = BuildContext(@"Errors\target_with_throwing_component.retl");

			ExecutionPackage package = context.BuildPackage();
			ExecutionResult result = package.Execute("transactionWithIsolationLevel");
			Assert.AreEqual(ExecutionStatus.Failure, result.Status);

			AssertRowCount(0);
		}

		[Test]
		public void Source_CanDirectlyExecute_AgainstWebService()
		{
			Server server = new Server(9090, "/", AppDomain.CurrentDomain.BaseDirectory);
			string binPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin");
			Directory.CreateDirectory(binPath);
			foreach (string file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory))
			{
				File.Copy(file, Path.Combine(binPath, Path.GetFileName(file)), true);
			}
			server.Start();
			try
			{
				List<Row> rows = new List<Row>();
				EtlConfigurationContext configurationContext = BuildContext(@"Integration\basic.retl");
				ExecutionPackage package = configurationContext.BuildPackage();
				using (package.EnterContext())
				{
					Pipeline pipeline = configurationContext.Pipelines["CopyUsers"];
					using (pipeline.EnterContext())
					{
						TestPipeLineStage pipeLineStage = TestOutput.GetPipelineStage(
							pipeline
							);
						pipeLineStage.OnProcess("Output", delegate(Row row) { rows.Add(row); });
						ManualResetEvent resetEvent = new ManualResetEvent(false);
						DataSource generator = configurationContext.Sources["WebServiceGenerator"];
						generator.RegisterForwarding(configurationContext.Targets["default"],
						                             pipeLineStage);
						pipeLineStage.OnComplete(delegate { resetEvent.Set(); });
						generator.Start(pipeline);
						resetEvent.WaitOne();
						rows.Sort(delegate(Row x, Row y) { return x["Id"].ToString().CompareTo(y["Id"]); });
						Assert.AreEqual(3, rows.Count);
						Assert.AreEqual("ALFKI", rows[0]["Id"]);
						Assert.AreEqual("Wrong id", rows[0]["Name"]);
						Assert.AreEqual("BUMP", rows[1]["Id"]);
						Assert.AreEqual("Goose", rows[1]["Name"]);
						Assert.AreEqual("Lump", rows[2]["Id"]);
						Assert.AreEqual("Of Rock", rows[2]["Name"]);
					}
				}
			}
			finally
			{
				server.Stop();
			}
		}

		private static void ExecutePackage(string name)
		{
			EtlConfigurationContext configurationContext = BuildContext(@"Integration\" + name + ".retl");
			ExecutionPackage package = configurationContext.BuildPackage();
			package.Execute("default");
		}
	}
}