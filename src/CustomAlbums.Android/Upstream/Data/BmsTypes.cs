namespace CustomAlbums.Data;

public readonly record struct RawNote(float Time, string Value, string Tone);
public readonly record struct ProcessedNote(int Id, decimal Time, string NoteUid, decimal Length, int Pathway, bool Blood);
