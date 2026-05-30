using System.Reflection;

namespace RemoteWorkerFarming
{
    public sealed class RemoteFarmWorkTarget : IRemoteDockWorkTarget
    {
        private readonly Workable workable;
        private readonly FieldInfo choreField;

        public RemoteFarmWorkTarget(Workable workable, FieldInfo choreField)
        {
            this.workable = workable;
            this.choreField = choreField;
        }


        public Chore RemoteDockChore
        {
            get
            {
                if (this.workable == null || this.choreField == null)
                {
                    return null;
                }

                return this.choreField.GetValue(this.workable) as Chore;
            }
        }

        public IApproachable Approachable
        {
            get
            {
                return this.workable as IApproachable;
            }
        }
    }
}
