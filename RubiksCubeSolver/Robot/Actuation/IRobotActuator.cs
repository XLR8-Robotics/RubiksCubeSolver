using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Robot.Actuation;

public interface IRobotActuator
{
    AppSettings Settings { get; }
    CubeOrientation Orientation { get; }
    Action<string>? OnCommand { get; set; }
    bool YawTurnersHomed { get; set; }
    bool PitchTurnersHomed { get; set; }
    CubeFace CurrentCameraFace { get; }

    void ResetOrientation();
    void ConfigureChannels();
    void AllServosOff();

    Task CommandAsync(string name, Func<CancellationToken, Task> command, CancellationToken cancellationToken);
    Task WaitAsync(CancellationToken cancellationToken);
    Task WaitUntilArmsNearAsync(ArmCalibration a, ArmCalibration b, bool retracted, CancellationToken cancellationToken);
    Task WaitUntilPairNearStartAsync(GripperCalibration a, GripperCalibration b, CancellationToken cancellationToken);

    Task TopBottomInAsync(CancellationToken cancellationToken, bool squeeze = false, int? squeezeExtraUs = null);
    Task TopBottomOutAsync(CancellationToken cancellationToken);
    Task LeftRightInAsync(CancellationToken cancellationToken, bool squeeze = true, int? squeezeExtraUs = null);
    Task LeftRightOutAsync(CancellationToken cancellationToken, bool clearOfCube = false);

    Task HoldPitchTurnersStillAsync(CancellationToken cancellationToken);
    Task HoldYawTurnersStillAsync(CancellationToken cancellationToken);
    Task PitchTurnersToStartAsync(CancellationToken cancellationToken);
    Task YawTurnersToStartAsync(CancellationToken cancellationToken);
    Task PitchSpin90Async(CancellationToken cancellationToken, bool opposite = false);
    Task YawSpin90Async(CancellationToken cancellationToken, bool opposite = false);

    Task SpinPairAsync(
        GripperCalibration a,
        GripperCalibration b,
        bool invertDirection,
        CancellationToken cancellationToken,
        int extraUs = 0,
        bool mirrorPair = true,
        bool pitchMatchedOpposite = false,
        bool yawMatchedOpposite = false,
        bool requireStartBeforeSpin = true);
    Task SpinPairYawFromCurrentAsync(GripperCalibration a, GripperCalibration b, bool invertDirection, CancellationToken cancellationToken);
    Task ReversePairToStartAsync(GripperCalibration a, GripperCalibration b, CancellationToken cancellationToken);

    bool? PairNearStart(GripperCalibration a, GripperCalibration b);
    (double TargetA, double TargetB) PairDualTumbleTargets(GripperCalibration gripperA, GripperCalibration gripperB, bool invertDirection, int extraUs = 0);
    double? GetPositionMicroseconds(byte port);

    void SetArm(ArmCalibration arm, bool inside, bool squeeze = false, int? squeezeExtraUs = null);
    void SetArmMicroseconds(ArmCalibration arm, double microseconds);
    void SetGripper(GripperCalibration gripper, bool turned);
    void SetGripperQuarterTurn(GripperCalibration gripper, bool prime);
    void NeutralGripper(GripperCalibration gripper);
    void AllArmsOut();
    void NeutralGrippers();
    Task RetractAllThenHomeTurnersAsync(CancellationToken cancellationToken);
}
