using System;
using System.Collections.Generic;
using System.Threading;

namespace Raven.Client.Http
{
    public class NodeSelector : IDisposable
    {
        private class NodeSelectorState
        {
            public readonly Topology Topology;
            public readonly int CurrentNodeIndex;
            public readonly List<ServerNode> Nodes;
            public readonly int[] Failures;
            public readonly int[] FastestRecords;
            public int Fastest;
            public int SpeedTestMode;

            public NodeSelectorState(int currentNodeIndex, Topology topology)
            {
                Topology = topology;
                CurrentNodeIndex = currentNodeIndex;
                Nodes = topology.Nodes;
                Failures = new int[topology.Nodes.Count];
                FastestRecords = new int[topology.Nodes.Count];
            }
        }        

        public Topology Topology => _state.Topology;

        private Timer _updateFastestNodeTimer;

        private NodeSelectorState _state;

        public NodeSelector(Topology topology)
        {
            _state = new NodeSelectorState(0, topology);
        }

        public void OnFailedRequest(int nodeIndex)
        {
            var state = _state;
            if (nodeIndex < 0 || nodeIndex >= state.Failures.Length)
                return; // probably already changed

            Interlocked.Increment(ref state.Failures[nodeIndex]);
        }

        public bool OnUpdateTopology(Topology topology, bool forceUpdate = false)
        {
            if (topology == null)
                return false;

            if (_state.Topology.Etag >= topology.Etag && forceUpdate == false)
                return false;

            var state = new NodeSelectorState(0, topology);

            Interlocked.Exchange(ref _state, state);

            return true;
        }

        public (int Index, ServerNode Node) GetPreferredNode()
        {
            var state = _state;
            for (int i = 0; i < state.Failures.Length; i++)
            {
                if (state.Failures[i] == 0)
                    return (i, state.Nodes[i]);
            }

            return UnlikelyEveryoneFaultedChoice(state);
        }

        private static ValueTuple<int, ServerNode> UnlikelyEveryoneFaultedChoice(NodeSelectorState state)
        {
            // if there are all marked as failed, we'll chose the first
            // one so the user will get an error (or recover :-) );
            return (0, state.Nodes[0]);
        }

        public (int Index, ServerNode Node) GetNodeBySessionId(int sessionId)
        {
            var state = _state;
            var index = sessionId % state.Topology.Nodes.Count;

            for (int i = index; i < state.Failures.Length; i++)
            {
                if (state.Failures[i] == 0 || state.Nodes[i].FailoverOnly == false)
                    return (i, state.Nodes[i]);
            }

            for (int i = 0; i < index; i++)
            {
                if (state.Failures[i] == 0 || state.Nodes[i].FailoverOnly == false)
                    return (i, state.Nodes[i]);
            }
            
            return GetPreferredNode();
        }

        public (int Index, ServerNode Node) GetFastestNode()
        {            
            var state = _state;
            if (state.Failures[state.Fastest] == 0)
                return (state.Fastest, state.Nodes[state.Fastest]);
            
            // if the fastest node has failures, we'll immeidately schedule
            // another run of finding who the fastest node is, in the meantime
            // we'll just use the server preferred node or failover as usual
            
            SwitchToSpeedTestPhase(null);
            return GetPreferredNode();
        }

        public void RestoreNodeIndex(int nodeIndex)
        {
            var state = _state;
            if (state.CurrentNodeIndex < nodeIndex)
                return; // nothing to do

            var stateFailure = state.Failures[nodeIndex];
            Interlocked.Add(ref state.Failures[nodeIndex], -stateFailure);// zero it
        }

        protected static void ThrowEmptyTopology()
        {
            throw new InvalidOperationException("Empty database topology, this shouldn't happen.");
        }

        private void SwitchToSpeedTestPhase(object _)
        {
            var state = _state;
            if (Interlocked.CompareExchange(ref state.SpeedTestMode, 1, 0) != 0)
                return;

            Array.Clear(state.FastestRecords,0, state.Failures.Length);

            Interlocked.Increment(ref state.SpeedTestMode);
        }

        public bool InSpeedTestPhase => _state.SpeedTestMode > 1;

        public void RecordFastest(int index, ServerNode node)
        {
            var state = _state;
            var stateFastest = state.FastestRecords;

            // the following two checks are to verify that things didn't move
            // while we were computing the fastest node, we verify that the index
            // of the fastest node and the identity of the node didn't change during
            // our check
            if (index < 0 || index >= stateFastest.Length)
                return;

            if (ReferenceEquals(node, state.Nodes[index]) == false)
                return;

            if (Interlocked.Increment(ref stateFastest[index]) >= 10)
            {
                SelectFastest(state, index);
            }

            if (Interlocked.Increment(ref state.SpeedTestMode) <= state.Nodes.Count * 10)
                return;

            //too many concurrent speed tests are happening
            var maxIndex = FindMaxIndex(state);
            SelectFastest(state, maxIndex);
        }

        private static int FindMaxIndex(NodeSelectorState state)
        {
            var stateFastest = state.FastestRecords;
            var maxIndex = 0;
            var maxValue = 0;

            for (var i = 0; i < stateFastest.Length; i++)
            {
                if (maxValue >= stateFastest[i])
                    continue;
                maxIndex = i;
                maxValue = stateFastest[i];
            }

            return maxIndex;
        }

        private void SelectFastest(NodeSelectorState state, int index)
        {
            state.Fastest = index;
            Interlocked.Exchange(ref state.SpeedTestMode, 0);

            _updateFastestNodeTimer.Change(TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
        }

        public void ScheduleSpeedTest()
        {
            if (_updateFastestNodeTimer == null)
            {
                _updateFastestNodeTimer = new Timer(SwitchToSpeedTestPhase, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            SwitchToSpeedTestPhase(null);
        }

        public void Dispose()
        {
            _updateFastestNodeTimer?.Dispose();
        }
    }
}