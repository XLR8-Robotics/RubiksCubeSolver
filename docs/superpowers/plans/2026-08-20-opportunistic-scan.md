# Opportunistic Cube Scan Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill cube stickers opportunistically during the existing F→R→B→L→F yaw loop instead of dual-hold merging every equatorial face.

**Architecture:** A sticker-index mask and per-face sample buffer let each photo write only visible stickers. `CubeScanSequence` drives expose/turn/home/pitch plus masked captures. Front/Back 2 and 8 are photographed while Top/Bottom are already retracted for rewind. Returning to Front homes yaw while keeping Left/Right holding so the last Front photo and the pitch phase share that pose.

**Tech Stack:** C# 14, .NET 10 Windows, WPF, OpenCvSharp, xUnit.

## Global Constraints

- Keep yaw-home after each 90° scan turn. Do not chain 180°/360° yaw without rewind.
- A photo must never overwrite stickers obstructed in that frame.
- Right and Left get all nine stickers from the Top/Bottom-hold photo.
- Top and Bottom pitch photographs stay full 1–9 and keep current pitch motion.
- Preserve `ScanFramesPerFace` and `ScanFrameGapMs` averaging.
- Reuse existing scan commands; add a yaw-home variant that keeps Left/Right holding.
- Do not add third-party dependencies.
- Do not commit unless the user explicitly requests a commit.

---

## File Structure

- Create `RubiksCubeSolver/Robot/Scan/ScanStickerMask.cs`: 0-based index lists for TB-hold, RL-hold, and all nine.
- Create `RubiksCubeSolver/Robot/Scan/ScanFaceBuffer.cs`: accumulate samples by index mask.
- Create `RubiksCubeSolver.Tests/ScanFaceBufferTests.cs`: mask merge behavior.
- Create `RubiksCubeSolver.Tests/CubeScanSequenceTests.cs`: recorded step order.
- Modify `RubiksCubeSolver/Robot/Scan/IScanStep.cs`: masked capture and expose/home-keep-RL session methods.
- Modify `RubiksCubeSolver/Robot/Scan/CubeScanSequence.cs`: opportunistic steps.
- Modify `RubiksCubeSolver/Robot/Commands/Scan/ScanYawTurnersHomeKeepFaceCommand.cs`: home while keeping RL hold.
- Modify `RubiksCubeSolver/Robot/RobotController.cs`: expose the keep-RL home.
- Modify `RubiksCubeSolver/ViewModels/MainViewModel.cs`: masked capture, live-net partial apply, complete-face check.

---

### Task 1: Mask constants and face buffer

**Files:**
- Create: `RubiksCubeSolver/Robot/Scan/ScanStickerMask.cs`
- Create: `RubiksCubeSolver/Robot/Scan/ScanFaceBuffer.cs`
- Create: `RubiksCubeSolver.Tests/ScanFaceBufferTests.cs`

**Interfaces:**
- Produces: `ScanStickerMask.AllNine`, `TopBottomHold`, `LeftRightHold` as `IReadOnlyList<int>`
- Produces: `ScanFaceBuffer.Write(Scalar[] incoming, IReadOnlyList<int> indices)`, `IsComplete`, `Samples`

- [ ] **Step 1: Write failing buffer tests**

Create `RubiksCubeSolver.Tests/ScanFaceBufferTests.cs`:

```csharp
using OpenCvSharp;
using RubiksCubeSolver.Robot.Scan;

namespace RubiksCubeSolver.Tests;

public class ScanFaceBufferTests
{
    static Scalar S(double v) => new(v, v, v);

    [Fact]
    public void TopBottomHold_SkipsStickersTwoAndEight()
    {
        Assert.Equal(new[] { 0, 2, 3, 4, 5, 6, 8 }, ScanStickerMask.TopBottomHold);
        Assert.DoesNotContain(1, ScanStickerMask.TopBottomHold);
        Assert.DoesNotContain(7, ScanStickerMask.TopBottomHold);
    }

    [Fact]
    public void LeftRightHold_IsOnlyStickersTwoAndEight()
    {
        Assert.Equal(new[] { 1, 7 }, ScanStickerMask.LeftRightHold);
    }

    [Fact]
    public void Write_TopBottomHold_DoesNotStoreObstructedStickers()
    {
        var buffer = new ScanFaceBuffer();
        var incoming = Enumerable.Range(0, 9).Select(i => S(i + 1)).ToArray();

        buffer.Write(incoming, ScanStickerMask.TopBottomHold);

        Assert.False(buffer.Written[1]);
        Assert.False(buffer.Written[7]);
        Assert.Equal(5, buffer.Samples[4].Val0);
        Assert.False(buffer.IsComplete);
    }

    [Fact]
    public void Write_LeftRightHold_FillsOnlyTwoAndEight_WithoutClobberingFourAndSix()
    {
        var buffer = new ScanFaceBuffer();
        var first = Enumerable.Range(0, 9).Select(i => S(10)).ToArray();
        var second = Enumerable.Range(0, 9).Select(i => S(20)).ToArray();

        buffer.Write(first, ScanStickerMask.TopBottomHold);
        buffer.Write(second, ScanStickerMask.LeftRightHold);

        Assert.Equal(10, buffer.Samples[3].Val0);
        Assert.Equal(10, buffer.Samples[5].Val0);
        Assert.Equal(20, buffer.Samples[1].Val0);
        Assert.Equal(20, buffer.Samples[7].Val0);
        Assert.True(buffer.IsComplete);
    }
}
```

- [ ] **Step 2: Run tests and confirm missing-type failure**

```powershell
dotnet test "RubiksCubeSolver.Tests\RubiksCubeSolver.Tests.csproj" --filter "FullyQualifiedName~ScanFaceBufferTests"
```

Expected: FAIL to compile because `ScanStickerMask` and `ScanFaceBuffer` do not exist.

- [ ] **Step 3: Implement mask and buffer**

- [ ] **Step 4: Run tests**

Expected: PASS.

- [ ] **Step 5: Do not commit** unless the user asks.

---

### Task 2: Sequence records opportunistic photos

**Files:**
- Modify: `RubiksCubeSolver/Robot/Scan/IScanStep.cs`
- Modify: `RubiksCubeSolver/Robot/Scan/CubeScanSequence.cs`
- Create: `RubiksCubeSolver.Tests/CubeScanSequenceTests.cs`

**Interfaces:**
- Consumes: `ScanStickerMask`
- Produces: `IScanSession.CaptureMaskedAsync`, `ScanExposeTopBottomHoldAsync`, `ScanExposeLeftRightHoldAsync`, `ScanYawTurnersHomeKeepRlHoldAsync`
- Removes: `CaptureDualHoldAsync`

- [ ] **Step 1: Write a failing sequence recording test** that expects:

1. TB expose, capture Front TopBottomHold
2. turn, TB expose, capture Right AllNine, home keep face
3. turn, TB expose, capture Back TopBottomHold, RL expose, capture Back LeftRightHold, home keep face
4. turn, TB expose, capture Left AllNine, home keep face
5. turn, home keep RL hold, capture Front LeftRightHold
6. pitch top, capture pitched U, return
7. pitch bottom, capture pitched D, return
8. finish hug

- [ ] **Step 2: Run tests, confirm compile/fail**

- [ ] **Step 3: Replace sequence steps and session interface**

- [ ] **Step 4: Run sequence tests**

Expected: PASS.

---

### Task 3: Session wiring and live net

**Files:**
- Modify: `RubiksCubeSolver/Robot/Commands/Scan/ScanYawTurnersHomeKeepFaceCommand.cs`
- Modify: `RubiksCubeSolver/Robot/RobotController.cs`
- Modify: `RubiksCubeSolver/ViewModels/MainViewModel.cs`

**Interfaces:**
- Produces: `ScanYawTurnersHomeKeepFaceCommand` home core with `restoreTbHold`
- Produces: `RobotController.ScanYawTurnersHomeKeepRlHoldAsync`
- Produces: `MainViewModel` masked capture into `ScanFaceBuffer`, `ApplyFace` with index mask
- Requires all six faces `IsComplete` before classify

- [ ] **Step 1: Add keep-RL home that skips the final TB in / RL out**

- [ ] **Step 2: Replace dual-hold capture with masked capture and partial `ApplyFace`**

- [ ] **Step 3: After Front TopBottomHold capture, keep the white-center log when index 4 was written**

- [ ] **Step 4: Run the full test suite**

```powershell
dotnet test "RubiksCubeSolver.Tests\RubiksCubeSolver.Tests.csproj"
```

Expected: all tests pass.

---

## Manual verification

Run a robot scan and confirm:

1. Front net fills around 2 and 8 first, then 2 and 8 appear after returning to Front.
2. Right and Left fill completely on their first photo.
3. Back 2 and 8 fill during the Back rewind pose.
4. Top and Bottom still fill from the pitch photos.
5. Solve still receives a complete 54-sticker cube.
