using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using Xunit;

namespace RocketWelder.SDK.Tests.Transport
{
    /// <summary>
    /// Tests for NNG transport implementations.
    /// </summary>
    public class NngTransportTests
    {
        #region Unit Tests - Constructor validation

        [Fact]
        public void NngFrameSink_Constructor_ThrowsOnNullSender()
        {
            Assert.Throws<ArgumentNullException>(() => new NngFrameSink(null!));
        }

        [Fact]
        public void NngFrameSource_Constructor_ThrowsOnNullReceiver()
        {
            Assert.Throws<ArgumentNullException>(() => new NngFrameSource(null!));
        }

        #endregion

        #region Integration Tests - Push/Pull pattern

        [Trait("Category", "Integration")]
        [Fact]
        public async Task PushPull_SingleFrame_RoundTrip()
        {
            var url = $"ipc:///tmp/nng-test-pushpull-{Guid.NewGuid():N}";
            var testData = Encoding.UTF8.GetBytes("Hello NNG Push/Pull!");

            using var pusher = NngFrameSink.CreatePusher(url, bindMode: true);

            // Give socket time to bind
            await Task.Delay(50);

            using var puller = NngFrameSource.CreatePuller(url, bindMode: false);

            // Give socket time to connect
            await Task.Delay(50);

            // Write frame
            await pusher.WriteFrameAsync(testData);

            // Read frame
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var received = await puller.ReadFrameAsync(cts.Token);

            Assert.Equal(testData, received.ToArray());
        }

        [Trait("Category", "Integration")]
        [Fact]
        public async Task PushPull_MultipleFrames_AllReceived()
        {
            var url = $"ipc:///tmp/nng-test-multi-{Guid.NewGuid():N}";
            var frames = new[]
            {
                Encoding.UTF8.GetBytes("Frame 1"),
                Encoding.UTF8.GetBytes("Frame 2"),
                Encoding.UTF8.GetBytes("Frame 3")
            };

            using var pusher = NngFrameSink.CreatePusher(url, bindMode: true);
            await Task.Delay(50);
            using var puller = NngFrameSource.CreatePuller(url, bindMode: false);
            await Task.Delay(50);

            // Write all frames
            foreach (var frame in frames)
            {
                await pusher.WriteFrameAsync(frame);
            }

            // Read all frames
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            foreach (var expected in frames)
            {
                var received = await puller.ReadFrameAsync(cts.Token);
                Assert.Equal(expected, received.ToArray());
            }
        }

        [Trait("Category", "Integration")]
        [Fact]
        public void PushPull_SyncOperations_Work()
        {
            var url = $"ipc:///tmp/nng-test-sync-{Guid.NewGuid():N}";
            var testData = Encoding.UTF8.GetBytes("Sync Test Data");

            using var pusher = NngFrameSink.CreatePusher(url, bindMode: true);
            Thread.Sleep(50);
            using var puller = NngFrameSource.CreatePuller(url, bindMode: false);
            Thread.Sleep(50);

            // Sync write
            pusher.WriteFrame(testData);

            // Sync read
            var received = puller.ReadFrame();

            Assert.Equal(testData, received.ToArray());
        }

        #endregion

        #region Integration Tests - Pub/Sub pattern
        // Note: NNG Pub/Sub tests are skipped because NNG's pub/sub pattern has the
        // "slow subscriber" problem - messages sent before the subscriber pipe is fully
        // established are silently dropped. There's no reliable notification mechanism
        // for when a subscriber has connected. This is a known NNG limitation.
        // In production, use a sync/handshake mechanism or Push/Pull for reliable delivery.

        [Trait("Category", "Integration")]
        [Fact(Skip = "NNG pub/sub has slow subscriber problem - messages dropped before connection established")]
        public async Task PubSub_WithEmptyTopic_ReceivesAllMessages()
        {
            var url = $"ipc:///tmp/nng-test-pubsub-{Guid.NewGuid():N}";
            var testData = Encoding.UTF8.GetBytes("Pub/Sub Test Message");

            using var publisher = NngFrameSink.CreatePublisher(url);
            await Task.Delay(100);

            // Subscribe with empty topic to receive all messages
            using var subscriber = NngFrameSource.CreateSubscriber(url, topic: Array.Empty<byte>());
            await Task.Delay(500);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var receiveTask = subscriber.ReadFrameAsync(cts.Token);
            await Task.Delay(100);

            for (int i = 0; i < 3; i++)
            {
                await publisher.WriteFrameAsync(testData);
                await Task.Delay(50);
            }

            var received = await receiveTask;
            Assert.Equal(testData, received.ToArray());
        }

        [Trait("Category", "Integration")]
        [Fact(Skip = "NNG pub/sub has slow subscriber problem - messages dropped before connection established")]
        public async Task PubSub_WithTopic_FiltersMessages()
        {
            var url = $"ipc:///tmp/nng-test-topic-{Guid.NewGuid():N}";
            var topic = Encoding.UTF8.GetBytes("mytopic:");
            var messageWithTopic = Encoding.UTF8.GetBytes("mytopic:Hello World");

            using var publisher = NngFrameSink.CreatePublisher(url);
            await Task.Delay(100);

            using var subscriber = NngFrameSource.CreateSubscriber(url, topic: topic);
            await Task.Delay(500);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var receiveTask = subscriber.ReadFrameAsync(cts.Token);
            await Task.Delay(100);

            for (int i = 0; i < 3; i++)
            {
                await publisher.WriteFrameAsync(messageWithTopic);
                await Task.Delay(50);
            }

            var received = await receiveTask;
            Assert.Equal(messageWithTopic, received.ToArray());
        }

        #endregion

        #region Disposal Tests

        [Trait("Category", "Integration")]
        [Fact]
        public async Task Sink_AfterDispose_ThrowsObjectDisposedException()
        {
            var url = $"ipc:///tmp/nng-test-dispose-sink-{Guid.NewGuid():N}";
            var pusher = NngFrameSink.CreatePusher(url);
            await Task.Delay(20);
            pusher.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                pusher.WriteFrame(new byte[] { 1, 2, 3 }));
        }

        [Trait("Category", "Integration")]
        [Fact]
        public async Task Source_AfterDispose_ThrowsObjectDisposedException()
        {
            var url = $"ipc:///tmp/nng-test-dispose-source-{Guid.NewGuid():N}";

            // Create pusher first (to bind)
            using var pusher = NngFrameSink.CreatePusher(url, bindMode: true);
            await Task.Delay(20);

            var puller = NngFrameSource.CreatePuller(url, bindMode: false);
            await Task.Delay(20);
            puller.Dispose();

            Assert.Throws<ObjectDisposedException>(() => puller.ReadFrame());
        }

        [Trait("Category", "Integration")]
        [Fact]
        public async Task AsyncDispose_Works()
        {
            var url = $"ipc:///tmp/nng-test-async-dispose-{Guid.NewGuid():N}";

            var pusher = NngFrameSink.CreatePusher(url);
            await Task.Delay(20);
            await pusher.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(() =>
                pusher.WriteFrame(new byte[] { 1, 2, 3 }));
        }

        #endregion

        #region TCP Transport Tests

        [Trait("Category", "Integration")]
        [Fact]
        public async Task PushPull_OverTcp_Works()
        {
            var port = 15555 + Random.Shared.Next(1000);
            var url = $"tcp://127.0.0.1:{port}";
            var testData = Encoding.UTF8.GetBytes("TCP Test Data");

            using var pusher = NngFrameSink.CreatePusher(url, bindMode: true);
            await Task.Delay(100);

            using var puller = NngFrameSource.CreatePuller(url, bindMode: false);
            await Task.Delay(100);

            await pusher.WriteFrameAsync(testData);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var received = await puller.ReadFrameAsync(cts.Token);

            Assert.Equal(testData, received.ToArray());
        }

        #endregion
    }
}
