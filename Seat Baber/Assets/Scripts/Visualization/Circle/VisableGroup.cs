using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VisableGroup : MonoBehaviour
{
    public Transform link1;
    public Transform link2;
    public int groupIndex;
    public float mult = 500;

    /// <summary>
    /// Apply a normalized sample value (e.g., spectrum magnitude) to this group,
    /// updating link1/link2 positions symmetrically around the origin.
    /// </summary>
    /// <param name="sample">Input value, typically in [0,1].</param>
    public void ApplySample(float sample)
    {
        if (link1 == null || link2 == null)
        {
            return;
        }

        Vector3 link1pos = link1.localPosition;
        Vector3 link2pos = link2.localPosition;

        Vector3 target1 = new Vector3(sample * mult, sample * mult, 0f);
        Vector3 target2 = new Vector3(-sample * mult, -sample * mult, 0f);

        link1.localPosition = Vector3.Lerp(link1pos, target1, 0.1f);
        link2.localPosition = Vector3.Lerp(link2pos, target2, 0.1f);
    }
}
