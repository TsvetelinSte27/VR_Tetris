using System.Globalization;
using System.Text;
using M2MqttUnity;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt.Messages;

public class MQTTcommunication : M2MqttUnityClient
{
    [Header("MQTT Topic")]
    public string topicToSubscribe = "scale";

    protected override void Start()
    {
        base.Start();

        // Set grid cellsize manually
        // GridController.Instance.cellSize = 0.12f;

        // Connect to broker when the scene starts
        Connect();
    }

    protected override void SubscribeTopics()
    {
        client.Subscribe(
            new[] { topicToSubscribe },
            new byte[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE }
        );

        Debug.Log("Subscribed to topic: " + topicToSubscribe);
    }

    protected override void DecodeMessage(string topic, byte[] message)
    {
        if (topic != topicToSubscribe)
        {
            return;
        }

        string msg = Encoding.UTF8.GetString(message);
        HandleMessage(msg);
    }

    private void HandleMessage(string msg)
    {
        Debug.Log(msg);

        if (!float.TryParse(msg, NumberStyles.Float, CultureInfo.InvariantCulture, out float newSize))
        {
            Debug.LogWarning("Invalid scale value received via MQTT: " + msg);
            return;
        }

        if (GridController.Instance == null)
        {
            Debug.LogWarning("GridController.Instance not found when applying MQTT scale.");
            return;
        }

        GridController.Instance.SetCellSize(newSize);
    }
}
