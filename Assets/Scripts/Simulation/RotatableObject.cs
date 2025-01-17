﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RotatableObject : MonoBehaviour, Rotatable, Connectable
{
    private List<Rotatable> connectedObjects = new List<Rotatable>();

    private float angularVelocity;
    private bool rotationActive = false;

    // Update is called once per frame
    void Update()
    {
        if (rotationActive)
        {
            //this.transform.GetComponentInChildren<Rigidbody>().AddTorque(this.transform.up * this.angularVelocity, ForceMode.Acceleration);
            //this.transform.RotateAround(this.transform.position, this.transform.up, this.angularVelocity * Time.deltaTime);
        }
    }

    public void addConnectedPart(Rotatable part)
    {
        connectedObjects.Add(part);
    }

    public List<Rotatable> getConnectedParts()
    {
        return this.connectedObjects;
    }

    public Transform getRootTransform() {
        return this.transform.root;
    }

    public void rotate(float angularVelocity, bool knowledgeBased) {
        this.angularVelocity = angularVelocity;
        this.rotationActive = true;

        foreach (Rotatable connectedObject in connectedObjects)
        {
            connectedObject.rotate(this.angularVelocity, knowledgeBased);
        }
    }

    public void stopRotation() {
        this.angularVelocity = 0.0f;
        this.rotationActive = false;

        foreach (Rotatable connectedObject in connectedObjects)
        {
            connectedObject.stopRotation();
        }
    }
}
