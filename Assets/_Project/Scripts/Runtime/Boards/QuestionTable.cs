using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// The table beside the question box (the QuestionTable model): questions go here face up once they've been pulled
    /// out and read, each turned to read the right way up for whoever put it down. Its place is
    /// <see cref="Notes.Table"/>; positions run 0..1 along and across the top.
    /// </summary>
    public sealed class QuestionTable : NoteSurface
    {
        public Transform Top;
        public Collider TopCollider;

        Transform _frame;

        /// <summary>
        /// A frame lying on the tabletop: +Z up out of it, +Y away from the front (the side facing the square's middle),
        /// so writing turned 0° reads the right way up from there.
        /// </summary>
        public override Transform Frame
        {
            get
            {
                if (!_frame && Top)
                {
                    _frame = new GameObject("Surface").transform;
                    _frame.SetParent(Top, false);
                    _frame.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
                }
                return _frame;
            }
        }

        /// <summary>Finds the model's parts by name (the prefab builder calls this so the references are saved).</summary>
        public void Bind()
        {
            Index = Notes.Table;
            Top = Find("Top");
            TopCollider = Top ? Top.GetComponent<Collider>() : null;
            if (Top && Top.GetComponent<MeshFilter>() is { sharedMesh: { } mesh })
                Size = new Vector2(mesh.bounds.size.x, mesh.bounds.size.z);
        }

        /// <summary>Where to stand at the table: <paramref name="distance"/> in front of it, looking down at it.</summary>
        public AvatarPose ReadingPose(float distance = 1f)
        {
            var front = transform.forward;
            var at = transform.position + front * distance;
            float yaw = Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg;
            return new AvatarPose(at, Mathf.Repeat(yaw, 360f), 38f);
        }
    }
}
