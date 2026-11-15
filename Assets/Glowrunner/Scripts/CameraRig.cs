using UnityEngine;

namespace Glowrunner
{
    /// <summary>Chase camera that follows the orb, with a toggleable overview of the whole maze.</summary>
    public class CameraRig : MonoBehaviour
    {
        GameManager game;
        bool overview;
        Vector3 velocity;

        public bool Overview => overview;

        public void Setup(GameManager g)
        {
            game = g;
            GetDesired(out var pos, out var look);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(look - pos);
        }

        public void Toggle() => overview = !overview;

        void LateUpdate()
        {
            if (game == null || game.Player == null) return;
            GetDesired(out var pos, out var look);
            transform.position = Vector3.SmoothDamp(transform.position, pos, ref velocity, 0.25f);
            var rot = Quaternion.LookRotation(look - pos);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-6f * Time.deltaTime));
        }

        void GetDesired(out Vector3 pos, out Vector3 look)
        {
            if (overview || game.WantsOverview)
            {
                pos = new Vector3(0f, 23f, -13f);
                look = new Vector3(0f, 0f, -1.2f);
                return;
            }
            // Keep the camera from drifting far past the maze edges.
            var p = game.Player.FlatPosition;
            float hx = game.Maze.Width * 0.5f - 4.5f, hz = game.Maze.Height * 0.5f - 4f;
            p.x = Mathf.Clamp(p.x, -hx, hx);
            p.z = Mathf.Clamp(p.z, -hz, hz);
            pos = p + new Vector3(0f, 10f, -6.5f);
            look = p + new Vector3(0f, 0f, 0.8f);
        }
    }
}
