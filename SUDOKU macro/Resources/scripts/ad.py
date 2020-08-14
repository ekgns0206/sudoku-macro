def callback(wform, frame, sprite, state, parameter):
    try:
        wform.InitStopWatch = True

        for x in range(1, 14):
            target = 'escape' + str(x).zfill(2)
            # wform.History = target
            try:
                if parameter is not None and target in parameter:
                    wform.app.Click(parameter[target])
                    return target + ' Click';
                    # wform.InitStopWatch = True
                    # wform.Sleep(3000)
            except Exception as e:
                continue
        return 'press escape'
    except Exception as e:
        return str(e)