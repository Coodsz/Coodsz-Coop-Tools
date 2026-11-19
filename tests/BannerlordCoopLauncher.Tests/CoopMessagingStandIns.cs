// Stand ins for the two Coop message interfaces (Common.Messaging) that the settings sync messages implement,
// so the tests can compile SettingsSyncMessages.cs as is and round trip it through protobuf. There is no
// IServerToClientCommand: newer Coop removed it, so a message that still used it would not build here.
namespace Common.Messaging;

public interface IMessage { }

public interface ICommand : IMessage { }
