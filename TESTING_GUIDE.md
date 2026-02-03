# Virtual ListView Font Changes - Testing & Troubleshooting Guide

## Quick Start Testing

### Step 1: Verify Build
```powershell
# The build should be successful
빌드 성공 (Build successful)
```
? **Current Status**: Build successful

### Step 2: Open Debug Output
1. In Visual Studio, go to **View → Output** (or press Ctrl+Alt+O)
2. Make sure dropdown shows **"Debug"** (not "Build")
3. Keep this window visible during testing

### Step 3: Load a Text File
1. Start the Uviewer application
2. Open any `.txt` file (preferably with several lines)
3. Verify it loads in the **ListView view** (not WebView2)
   - You should see lines displayed as individual items
   - NOT formatted as continuous wrapped HTML

### Step 4: Test Zoom Button
1. Click the **"+" (Zoom In)** button
2. **What you should see**:
   - ZoomLevelText changes (e.g., "32pt")
   - Text in ListView visibly **grows larger**
   - Scroll position stays at same location
   
3. **Debug Output should show**:
   ```
   [ZoomTextStyle] Applying font size change to virtual viewer: 32pt
   [ZoomTextStyle] Current scroll position before reset: X.X
   [ZoomTextStyle] Resetting ItemsSource to force container recreation
   [ZoomTextStyle] ItemsSource set to null
   [ZoomTextStyle] ItemsSource reassigned to _virtualTextLines (YYY items)
   [ContainerContentChanging] Called for index 0
   [ContainerContentChanging] Found TextBlock, current size: 30, applying: 32pt
   [ContainerContentChanging] Applied font: Yu Mincho, size: 32pt
   ... (repeats for multiple indices)
   [ZoomTextStyle] ScrollViewer was valid, attempting scroll position restore
   [ZoomTextStyle] Restoring scroll position to X.X
   [ZoomTextStyle] Font size change completed
   ```

## Detailed Testing Scenarios

### Scenario 1: Font Size Increase Works
**Objective**: Verify zoom "+" button makes text larger

**Steps**:
1. Open `.txt` file
2. Note current font size in label (e.g., "30pt")
3. Click "+" button once
4. Check label (should show "32pt")
5. **Verify text is visibly larger**
6. Repeat 3-4 times

**Success Criteria**:
- ? Label updates every time
- ? Text visibly grows each click
- ? No crashes or errors
- ? Debug output shows ContainerContentChanging callbacks

**If Fails** → Check debug output for:
- `[ZoomTextStyle] Error applying font size:` → Exception occurred
- `[ContainerContentChanging] TextBlock NOT FOUND` → Can't find TextBlock in container
- No `[ZoomTextStyle]` messages → Method not executing (button binding issue?)

---

### Scenario 2: Font Size Decrease Works
**Objective**: Verify zoom "-" button makes text smaller

**Steps**:
1. With text file open, click "-" button 5+ times
2. Watch font size in label decrease: 30 → 28 → 26 → ... → 8
3. Verify it doesn't go below 8pt (minimum size)

**Success Criteria**:
- ? Font size decreases by 2pt each click
- ? Text visibly shrinks on screen
- ? Minimum 8pt is enforced (no further decrease)

---

### Scenario 3: Font Family Toggle Works
**Objective**: Verify font changes between Yu Mincho and Yu Gothic Medium

**Steps**:
1. With text file open, note current font (should be Yu Mincho or Yu Gothic Medium)
2. Click **"Font"** button (font toggle)
3. Visually observe font changes
4. Click again to toggle back

**Expected Visual Difference**:
- **Yu Mincho**: Serif font, wider, more traditional
- **Yu Gothic Medium**: Sans-serif font, narrower, more modern

**Debug Output**:
```
[ChangeFont_Click] Changed font to: Yu Gothic Medium
[ChangeFont_Click] Refreshing virtual viewer with font: Yu Gothic Medium
[ChangeFont_Click] ItemsSource set to null
[ChangeFont_Click] ItemsSource reassigned
[ContainerContentChanging] Found TextBlock, current size: 30, applying: 32pt, font: Yu Gothic Medium
```

---

### Scenario 4: Scroll Position Preserved
**Objective**: Verify that after font changes, scroll position stays the same

**Steps**:
1. Open `.txt` file with many lines
2. Scroll to middle of text (not top, not bottom)
3. Note approximate visual position
4. Click zoom "+" button (or font button)
5. Verify text resizes but you're still at same relative position
6. Verify page number in status bar didn't change (or changed minimally)

**Success Criteria**:
- ? Scroll position visually preserved
- ? Same text line visible after size change
- ? Page number same or only off-by-one due to rounding

**Debug Indicators**:
```
[ZoomTextStyle] Current scroll position before reset: 450.5
[ZoomTextStyle] Restoring scroll position to 450.5
```

---

### Scenario 5: Rapid Size Changes
**Objective**: Verify no crashes with rapid font size changes

**Steps**:
1. Open `.txt` file
2. Rapidly click zoom "+" button 10 times
3. Rapidly click zoom "-" button 10 times
4. No lag, no crashes

**Success Criteria**:
- ? Application responsive
- ? All clicks register
- ? Final font size correct
- ? No memory leaks or freezing

---

### Scenario 6: Large File Responsiveness
**Objective**: Verify font changes work smoothly on large files

**Steps**:
1. Open a large `.txt` file (1MB+, 50k+ lines)
2. Let it fully load (status bar shows filename without "로딩 중...")
3. Click zoom buttons multiple times
4. Measure response time (should be <500ms per click)

**Success Criteria**:
- ? Font changes apply within 500ms
- ? No UI freezing
- ? No out-of-memory errors

---

### Scenario 7: Different Background Colors
**Objective**: Verify font styling works with all background colors

**Steps**:
1. Open `.txt` file
2. For each background color (White, Beige, Dark):
   - Click background color button
   - Verify text color contrasts properly
   - Click zoom button
   - Verify font size change still works with new colors

**Success Criteria**:
- ? Text readable on all backgrounds
- ? Font changes work on all backgrounds
- ? Colors persist after zoom

---

## Troubleshooting Guide

### Problem: Text Size Doesn't Change When Clicking Zoom Button

**Diagnosis Steps**:
1. Check debug output for `[ZoomTextStyle]` messages
   - **If missing**: Button click not reaching method → check XAML button binding
   - **If present**: Continue to next step

2. Check for `[ContainerContentChanging] TextBlock NOT FOUND`
   - **If yes**: ItemTemplate doesn't have TextBlock at expected location
   - **If no**: Continue to next step

3. Check for `[ZoomTextStyle] Error applying font size:`
   - **If yes**: Exception in ItemsSource reset logic → include error in issue report
   - **If no**: Check ZoomLevelText label is updating

4. Verify ZoomLevelText label shows new size
   - **If no**: `ZoomLevelText.Text = $"{_textFontSize}pt"` not executing
   - **If yes**: Label updates but text size doesn't → likely XAML hardcoding issue

**Common Solutions**:
- Clear Visual Studio cache: Delete `bin` and `obj` folders, rebuild
- Verify MainWindow.xaml doesn't have hardcoded FontSize in ItemTemplate
- Check that VirtualTextListView is visible (not collapsed)

---

### Problem: Scroll Position Lost After Font Change

**Diagnosis**:
1. Check debug output for:
   ```
   [ZoomTextStyle] ScrollViewer was valid
   [ZoomTextStyle] Restoring scroll position to X.X
   ```

2. If you see:
   ```
   [ZoomTextStyle] WARNING: Could not find ScrollViewer after ItemsSource reset
   ```
   - ScrollViewer reference lost during ItemsSource reset
   - This is recoverable but scroll position can't be restored

**Solutions**:
- Add delay before restoring: Change `await Task.Delay(150)` to `await Task.Delay(200)`
- FindScrollViewer() might be slow on large lists

---

### Problem: Text Flickers When Changing Font

**Expected Behavior**: ~200ms flicker is normal
- This is unavoidable with virtual ListView ItemsSource reset
- Performance trade-off for maintaining high performance with large files

**If Flicker is Excessive** (>1 second):
1. Check debug output timing
2. Increase delay: Change `await Task.Delay(150)` to `await Task.Delay(250)`

---

### Problem: Font Family Change Not Visible

**Diagnosis**:
1. Check that font toggle button is clicking (look for debug output)
2. Verify MainWindow.xaml ItemTemplate has no hardcoded FontFamily
3. Check that both fonts exist on system:
   - "Yu Mincho" (Japanese serif)
   - "Yu Gothic Medium" (Japanese sans-serif)

**Solutions**:
- If fonts missing, install from Windows: Settings → Fonts → Add fonts
- Fallback fonts configured: `'Yu Mincho', 'Yu Gothic Medium', sans-serif`

---

### Problem: Error Messages in Debug Output

**Common Errors and Solutions**:

```
[ContainerContentChanging] ItemContainer is null
```
- Virtualization hasn't materialized container yet
- Usually temporary, next ContainerContentChanging should succeed

```
[ZoomTextStyle] Error applying font size: Object reference not set
```
- _virtualScrollViewer is null after ItemsSource reset
- Solution: Already handled - code searches for ScrollViewer again

```
[ZoomTextStyle] Error applying font size: The parameter is incorrect
```
- Invalid font name or invalid size value
- Check: `_currentFontFamily` is valid string
- Check: `_textFontSize` is between 8-60

---

## Performance Benchmarks

**Expected Performance** (on typical machine):
- Font size change: < 300ms total
- Font family change: < 300ms total  
- Scroll position restoration: < 100ms
- Full re-render: < 200ms (virtualization)

**If Slower**:
1. Close other applications
2. Check for antivirus scanning delays
3. Profile with Stopwatch in debug output

---

## Debug Output Interpretation

### Success Pattern
```
[ZoomTextStyle] Applying font size change to virtual viewer: 32pt
[ZoomTextStyle] Current scroll position before reset: 450.5
[ZoomTextStyle] Resetting ItemsSource to force container recreation
[ZoomTextStyle] ItemsSource set to null
[ZoomTextStyle] ItemsSource reassigned to _virtualTextLines (105 items)
[ZoomTextStyle] ItemsSource reassigned, waiting for containers to be created
[ContainerContentChanging] Called for index 0
[ContainerContentChanging] Found TextBlock, current size: 30, applying: 32pt
[ContainerContentChanging] Applied font: Yu Mincho, size: 32pt
[ContainerContentChanging] After setting - FontSize is now: 32
[ContainerContentChanging] Called for index 1
... (more indices)
[ZoomTextStyle] ScrollViewer was valid, attempting scroll position restore
[ZoomTextStyle] Restoring scroll position to 450.5
[ZoomTextStyle] Scroll position restored
[ZoomTextStyle] Font size change completed
```

### Failure Pattern to Watch For
```
[ZoomTextStyle] Applying font size change...
[ZoomTextStyle] Resetting ItemsSource...
[ZoomTextStyle] ItemsSource set to null
[ZoomTextStyle] ItemsSource reassigned...
[ContainerContentChanging] Called for index 0
[ContainerContentChanging] TextBlock NOT FOUND  <-- PROBLEM!
[ContainerContentChanging] Called for index 1
[ContainerContentChanging] TextBlock NOT FOUND  <-- PROBLEM!
```
- If you see this, the ItemTemplate structure is wrong
- TextBlock not in expected location in visual tree

---

## Checklist Before Reporting Issues

If font changes still don't work, verify:

- [ ] Build is successful (`빌드 성공`)
- [ ] Opening `.txt` file (not `.md`, `.html`, `.epub`)
- [ ] Virtual ListView shows (lines as separate items, not HTML flow)
- [ ] Zoom button clicking (try button in other UI to verify clicking works)
- [ ] Output window set to "Debug" pane
- [ ] All expected debug messages appear in output
- [ ] ZoomLevelText label updates (shows new size)
- [ ] No exception messages in debug output

If all above verified but still not working:
- Save debug output (Ctrl+A, Ctrl+C in Output window)
- Check if MainWindow.xaml still has old hardcoded FontSize
- Try clean rebuild: Delete `bin` and `obj`, rebuild solution

---

## Next Steps

1. **Run the application**
2. **Execute Scenario 1** (Font Size Increase)
3. **Report results** with:
   - What changed visually (did text grow?)
   - What appeared in debug output
   - Any error messages
   - Video or screenshot if possible

4. **If successful**, run remaining scenarios
5. **If failed**, provide debug output and which scenario failed

Good luck with testing! The ItemsSource reset pattern should make these changes work.
