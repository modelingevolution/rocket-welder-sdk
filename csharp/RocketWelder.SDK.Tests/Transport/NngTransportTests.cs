using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using Xunit;
using Xunit.Abstractions;

namespace RocketWelder.SDK.Tests.Transport
{
    /// <summary>
    /// Tests for NNG transport implementations.
    /// </summary>
    public class NngTransportTests
    {
        private readonly ITestOutputHelper _output;

        public NngTransportTests(ITestOutputHelper output)
        {
            _output = output;
        }

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

        #region Integration Tests - Push/Pull pattern (IPC)

        [Trait("Category", "Integration")]
        [Fact]
        public async Task PushPull_IPC_SingleFrame_RoundTrip()
        {
            var url = $"ipc:///tmp/nng-test-pushpull-{Guid.NewGuid():N}";
            var testData = Encoding.UTF8.GetBytes("Hello NNG Push/Pull!");

            using var pusher = NngFrameSink.CreatePusher(url, bindMode: true);
            await Task.Delay(50);

            using var puller = NngFrameSource.CreatePuller(url, bindMode: false);
            await Task.Delay(50);

            await pusher.WriteFrameAsync(testData);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var received = await puller.ReadFrameAsync(cts.Token);

            Assert.Equal(testData, received.ToArray());
        }

        [Trait("Category", "Integration")]
        [Fact]
        public async Task PushPull_IPC_MultipleFrames_AllReceived()
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

            foreach (var frame in frames)
            {
                await pusher.WriteFrameAsync(frame);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            foreach (var expected in frames)
            {
                var received = await puller.ReadFrameAsync(cts.Token);
                Assert.Equal(expected, received.ToArray());
            }
        }

        [Trait("Category", "Integration")]
        [Fact]
        public void PushPull_IPC_SyncOperations_Work()
        {
            var url = $"ipc:///tmp/nng-test-sync-{Guid.NewGuid():N}";
            var testData = Encoding.UTF8.GetBytes("Sync Test Data");

            using var pusher = NngFrameSink.CreatePusher(url, bindMode: true);
            Thread.Sleep(50);
            using var puller = NngFrameSource.CreatePuller(url, bindMode: false);
            Thread.Sleep(50);

            pusher.WriteFrame(testData);
            var received = puller.ReadFrame();

            Assert.Equal(testData, received.ToArray());
        }

        #endregion

        #region Integration Tests - Push/Pull pattern (TCP)

        [Trait("Category", "Integration")]
        [Fact]
        public async Task PushPull_TCP_SingleFrame_RoundTrip()
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

        #region Integration Tests - Pub/Sub pattern (IPC)
        // NOTE: NNG Pub/Sub requires proper timing for subscription propagation.
        // The subscriber must connect and subscribe before the publisher sends.
        // We use retry loops to handle the timing window.

        [Trait("Category", "Integration")]
        [Fact]
        public async Task PubSub_IPC_WithEmptyTopic_ReceivesAllMessages()
        {
            var url = $"ipc:///tmp/nng-test-pubsub-{Guid.NewGuid():N}";
            var testData = Encoding.UTF8.GetBytes("Pub/Sub Test Message");

            _output.WriteLine($"Creating publisher at {url}");
            using var publisher = NngFrameSink.CreatePublisher(url);

            _output.WriteLine("Creating subscriber");
            using var subscriber = NngFrameSource.CreateSubscriber(url, topic: Array.Empty<byte>());

            // Wait for subscriber to connect using pipe notifications
            _output.WriteLine("Waiting for subscriber to connect...");
            var connected = await publisher.WaitForSubscriberAsync(TimeSpan.FromSeconds(5));
            Assert.True(connected, "Subscriber should have connected");
            _output.WriteLine($"Subscriber connected! Count: {publisher.SubscriberCount}");

            // Additional delay for subscription to propagate through the protocol layer
            // This is a known NNG pub/sub timing issue - subscription needs time to reach publisher
            await Task.Delay(500);

            // Publish message first (non-blocking for pub/sub)
            _output.WriteLine("Publishing message");
            publisher.WriteFrame(testData);

            // Small delay then receive synchronously (avoids async context issues)
            await Task.Delay(100);
            var received = subscriber.ReadFrame();
            _output.WriteLine($"Received {received.Length} bytes");

            Assert.Equal(testData, received.ToArray());
        }

        [Trait("Category", "Integration")]
        [Fact]
        public async Task PubSub_IPC_WithTopic_FiltersMessages()
        {
            var url = $"ipc:///tmp/nng-test-topic-{Guid.NewGuid():N}";
            var topic = Encoding.UTF8.GetBytes("mytopic:");
            var messageWithTopic = Encoding.UTF8.GetBytes("mytopic:Hello World");

            _output.WriteLine($"Creating publisher at {url}");
            using var publisher = NngFrameSink.CreatePublisher(url);

            _output.WriteLine($"Creating subscriber with topic '{Encoding.UTF8.GetString(topic)}'");
            using var subscriber = NngFrameSource.CreateSubscriber(url, topic: topic);

            // Wait for subscriber to connect
            var connected = await publisher.WaitForSubscriberAsync(TimeSpan.FromSeconds(5));
            Assert.True(connected, "Subscriber should have connected");
            _output.WriteLine($"Subscriber connected! Count: {publisher.SubscriberCount}");

            // Additional delay for subscription to propagate
            await Task.Delay(500);

            _output.WriteLine("Publishing message with topic");
            publisher.WriteFrame(messageWithTopic);

            await Task.Delay(100);
            var received = subscriber.ReadFrame();
            _output.WriteLine($"Received {received.Length} bytes");

            Assert.Equal(messageWithTopic, received.ToArray());
        }

        [Trait("Category", "Integration")]
        [Fact]
        public async Task PubSub_IPC_MultipleMessages_AllReceived()
        {
            var url = $"ipc:///tmp/nng-test-pubsub-multi-{Guid.NewGuid():N}";
            var messages = new[]
            {
                Encoding.UTF8.GetBytes("Message 1"),
                Encoding.UTF8.GetBytes("Message 2"),
                Encoding.UTF8.GetBytes("Message 3")
            };

            using var publisher = NngFrameSink.CreatePublisher(url);
            using var subscriber = NngFrameSource.CreateSubscriber(url, topic: Array.Empty<byte>());

            var connected = await publisher.WaitForSubscriberAsync(TimeSpan.FromSeconds(5));
            Assert.True(connected, "Subscriber should have connected");

            // Wait for subscription to propagate
            await Task.Delay(500);

            // Send all messages
            foreach (var msg in messages)
            {
                publisher.WriteFrame(msg);
            }

            // Receive all messages
            await Task.Delay(100);
            var receivedMessages = new List<byte[]>();
            for (int i = 0; i < messages.Length; i++)
            {
                var received = subscriber.ReadFrame();
                receivedMessages.Add(received.ToArray());
            }

            // Verify all messages received (order should be preserved)
            Assert.Equal(messages.Length, receivedMessages.Count);
            for (int i = 0; i < messages.Length; i++)
            {
                Assert.Equal(messages[i], receivedMessages[i]);
            }
        }

        #endregion

        #region Integration Tests - Pub/Sub pattern (TCP)

        [Trait("Category", "Integration")]
        [Fact]
        public async Task PubSub_TCP_SingleMessage_RoundTrip()
        {
            var port = 16555 + Random.Shared.Next(1000);
            var url = $"tcp://127.0.0.1:{port}";
            var testData = Encoding.UTF8.GetBytes("TCP Pub/Sub Test");

            _output.WriteLine($"Creating publisher at {url}");
            using var publisher = NngFrameSink.CreatePublisher(url);

            _output.WriteLine("Creating subscriber");
            using var subscriber = NngFrameSource.CreateSubscriber(url, topic: Array.Empty<byte>());

            // Wait for subscriber to connect
            var connected = await publisher.WaitForSubscriberAsync(TimeSpan.FromSeconds(5));
            Assert.True(connected, "Subscriber should have connected");
            _output.WriteLine($"Subscriber connected! Count: {publisher.SubscriberCount}");

            // Additional delay for subscription to propagate
            await Task.Delay(500);

            publisher.WriteFrame(testData);

            await Task.Delay(100);
            var received = subscriber.ReadFrame();
            _output.WriteLine($"Received {received.Length} bytes");

            Assert.Equal(testData, received.ToArray());
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

        #region Subscriber Count Tests

        [Trait("Category", "Integration")]
        [Fact]
        public async Task Publisher_SubscriberCount_TracksConnections()
        {
            var url = $"ipc:///tmp/nng-test-subcount-{Guid.NewGuid():N}";

            using var publisher = NngFrameSink.CreatePublisher(url);
            Assert.Equal(0, publisher.SubscriberCount);

            using var subscriber1 = NngFrameSource.CreateSubscriber(url, topic: Array.Empty<byte>());
            await publisher.WaitForSubscriberAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, publisher.SubscriberCount);

            using var subscriber2 = NngFrameSource.CreateSubscriber(url, topic: Array.Empty<byte>());
            await Task.Delay(100); // Wait for second connection
            Assert.Equal(2, publisher.SubscriberCount);
        }

        #endregion
    }
}
