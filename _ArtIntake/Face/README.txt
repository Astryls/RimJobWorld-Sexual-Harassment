FACE tattoo art intake
======================
Drop FACE-tattoo DDS files here (the ones you listed as FACE).

Naming — RimWorld renders tattoos as a Graphic_Multi, so each tattoo is a
3-rotation SET (west auto-mirrors east):

    <Name>_south.dds   (front / facing camera)
    <Name>_north.dds   (back)
    <Name>_east.dds     (side)

If you only have ONE image for a mark, drop it as <Name>_south.dds and say so —
I'll wire it single-rotation (it just won't change when the pawn turns).

Notes
-----
- RimWorld 1.6 loads .dds natively and PREFERS it over .png, so these work in
  game as-is once named + placed under Textures/ and pointed at by a TattooDef.
- FACE tattoos are masked to the pawn's HEAD graphic (headType.graphicPath), so
  the art must sit on the vanilla head canvas to line up.
- I cannot resize/convert .dds. If a file is the wrong size I can only report its
  dimensions (from the header) — resizing needs the source as PNG/SVG, or you
  resize it externally and re-drop.
- This folder (_ArtIntake) is OUTSIDE Textures/, so RimWorld ignores it. Once a
  set is correct I move it to Textures/RJWSH/Tattoos/Face/ and add the TattooDef.
