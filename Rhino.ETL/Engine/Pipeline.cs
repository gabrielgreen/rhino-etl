namespace Rhino.ETL.Engine
{
	using System.Collections.Generic;
	using Commons;
	using Exceptions;

	public class Pipeline : ContextfulObjectBase<Pipeline>
	{
		private readonly IList<PipelineAssociation> associations = new List<PipelineAssociation>();

		public delegate void PipelineCompleted(Pipeline completed);

		public event PipelineCompleted Completed = delegate { };

		private readonly string name;
		private CountdownLatch destinationToComplete;

		public Pipeline(string name)
		{
			this.name = name;
			EtlConfigurationContext.Current.AddPipeline(name, this);
		}

		public override string Name
		{
			get { return name; }
		}


		public IList<PipelineAssociation> Associations
		{
			get { return associations; }
		}

		public void AddAssociation(PipelineAssociation association)
		{
			associations.Add(association);
		}

		public void Validate(ICollection<string> messages)
		{
			using (EnterContext())
			{
				foreach (PipelineAssociation association in associations)
				{
					association.Validate(messages);
				}
			}
		}

		private IEnumerable<T> GetFromAssoicationsAll<T>()
		{
			foreach (PipelineAssociation association in associations)
			{
				if (association.FromInstance is T)
					yield return (T) association.FromInstance;
				if (association.ToInstance is T)
					yield return (T) association.ToInstance;
			}
		}

		public void PerformSecondStagePass()
		{
			using (EnterContext())
			{
				foreach (PipelineAssociation association in associations)
				{
					association.PerformSecondStagePass();
				}
				EnsureCanGetAllConnections();
			}
		}

		private void EnsureCanGetAllConnections()
		{
			Dictionary<Connection, int> connectionCount = new Dictionary<Connection, int>();
			foreach (IConnectionUser useConnection in GetFromAssoicationsAll<IConnectionUser>())
			{
				if(useConnection.ConnectionInstance==null)
					continue;
				if (connectionCount.ContainsKey(useConnection.ConnectionInstance) == false)
					connectionCount.Add(useConnection.ConnectionInstance, 0);
				connectionCount[useConnection.ConnectionInstance] += 1;
			}
			//Can't do it in validation stage, since this require that we will have a fully
			//connected graph
			foreach (KeyValuePair<Connection, int> pair in connectionCount)
			{
				if (pair.Key.ConcurrentConnections < pair.Value)
				{
					throw new TooManyConcurrentConnectionsRequiredException(
						string.Format("Pipeline '{0}' requires {1} concurrent connections from '{2}', but limit is {3}",
						              Name, pair.Value, pair.Key.Name, pair.Key.ConcurrentConnections));
				}
			}
		}

		public void Prepare()
		{
			int destinationCount = associations.Count;
			destinationToComplete = new CountdownLatch(destinationCount);
		}

		public void Start(Target target)
		{
			if (associations.Count == 0)
			{
				Completed(this);
				return;
			}
			if (AcquireAllConnections(target) == false)
				return;
			foreach (PipelineAssociation association in associations)
			{
				association.ConnectEnds(target, this);
			}
			foreach (PipelineAssociation association in associations)
			{
				association.Completed += AssociationCompleted;
			}
			foreach (DataSource value in EtlConfigurationContext.Current.Sources.Values)
			{
				DataSource cSharpSpec_21_5_2_Damn_It = value; 
				ExecutionPackage.Current.RegisterForExecution(target,
				                                              delegate { cSharpSpec_21_5_2_Damn_It.Start(this); }
					);
			}
		}

		private void AssociationCompleted(PipelineAssociation association)
		{
			int count = destinationToComplete.Set();
			if (count == 0)
				Completed(this);
		}


		private bool AcquireAllConnections(Target target)
		{
			List<IConnectionUser> aquiredConnection = new List<IConnectionUser>();

			foreach (IConnectionUser connectionUser in GetFromAssoicationsAll<IConnectionUser>())
			{
				if (connectionUser.TryAcquireConnection(this))
				{
					aquiredConnection.Add(connectionUser);
				}
				else
				{
					Logger.WarnFormat(
						"Could not aquired all connections in pipeline '{0}', will retry when the next pipeline completes", Name);
					foreach (IConnectionUser user in aquiredConnection)
					{
						user.ReleaseConnection(this);
					}
					ExecutionPackage.Current.ExecuteOnPipelineCompleted(delegate { Start(target); });
					return false;
				}
			}
			return true;
		}
	}
}