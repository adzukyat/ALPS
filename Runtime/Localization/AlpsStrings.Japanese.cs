using System.Collections.Generic;

namespace AdzukiSoft.ALPS
{
    public static partial class AlpsStrings
    {
        /// <summary>Japanese translation. Keys match <see cref="BuildEnglish"/>.</summary>
        private static Dictionary<string, string> BuildJapanese()
        {
            return new Dictionary<string, string>
            {
                // --- Units -------------------------------------------------
                { "unit.beats", "拍" },
                { "unit.light", "灯" },

                // --- Shared labels and options ----------------------------
                { "common.order", "並び順" },
                { "common.order.normal", "通常" },
                { "common.order.reverse", "逆順" },
                { "common.order.random", "ランダム" },
                { "common.symmetric", "左右対称" },
                { "common.seed", "シード" },
                { "common.radius", "半径" },
                { "common.rotation", "回転" },
                { "common.rotationX", "X回転" },
                { "common.rotationY", "Y回転" },
                { "common.rotationZ", "Z回転" },
                { "common.width", "幅" },
                { "common.height", "高さ" },
                { "common.start", "始点" },
                { "common.end", "終点" },
                { "common.speed", "速度" },
                { "common.fade", "フェード" },
                { "common.palette", "パレット" },
                { "common.profile", "プロファイル" },
                { "common.delete", "削除" },
                { "common.duplicate", "複製" },
                { "common.range", "レンジ" },
                { "common.spread", "広がり" },
                { "common.rise", "上り" },
                { "common.fall", "下り" },

                // --- Arrangement (container) -------------------------------
                { "arrangement.shape", "形状" },
                { "arrangement.shape.off", "オフ" },
                { "arrangement.shape.line", "直線" },
                { "arrangement.shape.circle", "円" },
                { "arrangement.shape.polygon", "多角形" },
                { "arrangement.shape.rectangle", "矩形" },
                { "arrangement.shape.grid", "グリッド" },
                { "arrangement.shape.tip", "子オブジェクトを並べる形です。オフでは子オブジェクトを動かしません。円は角度を360°未満にすると円弧になります。" },
                { "arrangement.spacing", "配置" },
                { "arrangement.spacing.endToEnd", "端から端" },
                { "arrangement.spacing.even", "均等割り" },
                { "arrangement.spacing.tip", "端から端: 最初と最後のオブジェクトを線の両端に置きます。均等割り: 全体をオブジェクトの数で等分し、それぞれの真ん中に置きます。" },
                { "arrangement.order.tip", "S(広がり)の値をどの順番でオブジェクトに割り当てるかです。置く場所の順番は変わりません。" },
                { "arrangement.symmetric.tip", "真ん中で折り返し、左右で同じ位置のオブジェクトに同じ値を割り当てます。通常は真ん中から、逆順は両端から数え、ランダムは左右の組ごとに並べ替えます。前半のY回転とZ回転は反転します。" },
                { "arrangement.seed.tip", "ランダムな並び順を変えます。" },
                { "arrangement.card.shape.title", "形" },
                { "arrangement.card.shape.desc", "並べる線や円の大きさです。始点、終点、半径、幅と奥行きはシーン上のハンドルでも動かせます。" },
                { "arrangement.sides", "辺の数" },
                { "arrangement.columns", "列数" },
                { "arrangement.angle.tip", "形全体を回します。円弧では円弧の真ん中の向きです。" },
                { "arrangement.sweep", "角度" },
                { "arrangement.sweep.tip", "円のうち何度分に並べるかです。360°未満で円弧になります。" },
                { "arrangement.depth", "奥行き" },
                { "arrangement.card.facing.title", "向き" },
                { "arrangement.card.facing.desc", "オブジェクトの向きです。X/Y/Z回転は向きを決めた後に各オブジェクトの軸で回り、Sで1つずつ変えるとファンになります。" },
                { "arrangement.facing", "向き" },
                { "arrangement.facing.asIs", "そのまま" },
                { "arrangement.facing.outward", "外向き" },
                { "arrangement.facing.inward", "内向き" },
                { "arrangement.facing.forward", "進行方向" },
                { "arrangement.facing.target", "注視点" },
                { "arrangement.facing.tip", "そのまま: コンテナーと同じ向き。外向き/内向き: 形の外側か内側。進行方向: 並ぶ方向。注視点: 指定した点の方。どれも前(+Z)をその方向へ向け、上はできるだけ上のままにします。" },
                { "arrangement.target", "注視点" },
                { "arrangement.card.offset.title", "位置" },
                { "arrangement.card.offset.desc", "形の上からずらす量です。Sで1つずつ変えると、高さなら階段や螺旋、外側なら左右対称と合わせてV字になります。" },
                { "arrangement.outward", "外側" },
                { "arrangement.outward.tip", "形の外側へずらします。直線では前(+Z)側です。" },
                { "arrangement.spread.first", "最初のオブジェクト" },
                { "arrangement.spread.last", "最後のオブジェクト" },

                // --- Clip --------------------------------------------------
                { "clip.bpmOverride", "BPMオーバーライド" },
                { "clip.bpmOverride.tip", "全体BPMと違う値にすると、このクリップだけそのBPMで動きます。全体BPMと同じ値に戻すと全体BPMに追従します。" },
                { "clip.globalBpm", "全体BPM" },
                { "clip.globalBpm.tip", "Timeline全体のテンポです。全てのクリップはそれぞれの開始位置を1拍目として拍を数えます。" },
                { "clip.seed.tip", "ランダムな並び順を変えます。同じシードのクリップは同じ並びになり、クリップを動かしても並びは変わりません。" },
                { "clip.symmetric.tip", "真ん中で折り返し、左右で同じ位置の灯体に同じ順番を割り当てます。通常は真ん中から、逆順は両端から数え、ランダムは左右の組ごとに並べ替えます。前半の灯体はパンが反転します。" },
                { "clip.fade.tip", "クリップの開始から効き切るまでと、終了前に弱まり始めてから終わるまでの拍数です。何も効果のないクリップとブレンドしたときと同じように、ムーブや色を含む全ての効果にかかります。" },
                { "clip.common.title", "共通設定" },
                { "clip.common.desc", "Rangeとパレットの動作を調整できます。" },
                { "clip.editorUnavailable", "このクリップのエディタを表示できませんでした。" },
                { "clip.multiEdit", "{0}個のクリップを同時に編集しています。値が異なる項目は「{1}」で表示され、変更した項目だけが全てのクリップに反映されます。" },
                { "clip.profileSync", "プロファイルに追従" },
                { "clip.profileSync.tip", "オンの間はプロファイルの効果を再生し、編集もプロファイルに書き込みます。" },
                { "clip.load", "読込" },
                { "clip.load.tip.single", "プロファイルの内容をこのクリップに複製します。" },
                { "clip.load.tip.multi", "それぞれのプロファイルの内容を各クリップに複製します。" },
                { "clip.save", "保存" },
                { "clip.save.tip.single", "このクリップの内容をプロファイルに書き込みます。プロファイルが未設定なら新しく作ります。" },
                { "clip.save.tip.multi", "複数のクリップを選択している間は保存できません。" },
                { "clip.save.dialogTitle", "プロファイルを保存" },
                { "clip.save.dialogPrompt", "保存先を選んでください。" },

                // --- Add effect catalog -----------------------------------
                { "addEffect.title", "効果を追加" },

                // --- Effect names and descriptions ------------------------
                { "effect.move.name", "ムーブ" },
                { "effect.cone.name", "コーン" },
                { "effect.color.name", "カラー" },
                { "effect.brightness.name", "明るさ" },
                { "effect.flicker.name", "フリッカー" },
                { "effect.gobo.name", "ゴボ" },
                { "effect.move.desc", "灯体の向きを指定します。円を描いたり、ユーザーの方向に追跡させることもできます。" },
                { "effect.cone.desc", "光線の幅と長さを調整します。" },
                { "effect.color.desc", "灯体の色を設定します。複数追加すると、複雑なアニメーションも制作できます。" },
                { "effect.brightness.desc", "灯体の明るさを変更します。" },
                { "effect.flicker.desc", "光がランダムにちらつきます。" },
                { "effect.gobo.desc", "光に模様を投影します。回転させることもできます。" },
                { "effect.parity.even", "（偶数）" },
                { "effect.parity.odd", "（奇数）" },

                // --- Effect view ------------------------------------------
                { "effect.move.mode.angle", "角度指定" },
                { "effect.move.mode.circle", "円" },
                { "effect.move.mode.track", "ユーザー追跡" },
                { "effect.move.phaseDiff", "位相差" },
                { "effect.move.centerTilt", "中心 Tilt" },
                { "effect.move.centerPan", "中心 Pan" },
                { "effect.move.aspect", "縦横比" },
                { "effect.move.userName", "ユーザー名" },
                { "effect.move.followSpeed", "追従速度" },
                { "effect.cone.length", "長さ" },
                { "effect.brightness.blackoutReturn", "復路で消灯" },
                { "effect.flicker.strength", "強さ" },
                { "effect.fixtureStagger", "灯体間ズレ" },
                { "effect.gobo.rotationSpeed", "回転速度" },
                { "effect.gobo.perTurn", "/ 1回転" },
                { "effect.phaseOffset", "位相オフセット" },
                { "effect.phaseOffset.tip", "この効果の動きを周期の何%遅らせるか。偶数と奇数に分けた片方を 50% にすると交互に動きます。" },

                // --- Phase settings ---------------------------------------
                { "phase.mode", "モード" },
                { "phase.mode.wave", "波形" },
                { "phase.mode.random", "ランダム" },
                { "phase.shares", "配分" },
                { "phase.easing", "イージング" },
                { "phase.group", "灯体単位" },
                { "phase.delay", "ディレイ" },
                { "phase.beatsFlag.letter", "拍" },
                { "phase.beatsFlag.tip", "拍で指定" },
                { "phase.perCycle", "/ 1周期" },
                { "phase.fireChance", "確率" },
                { "phase.fireChance.tip", "1周期ごとに波が起きる確率です。灯体(並び順の位置)ごと、周期ごとに決まり、起きなかった周期は波の一番下で待ちます。反転中は一番上で待ちます。" },
                { "phase.inverse", "反転" },
                { "phase.part.rise", "上り" },
                { "phase.part.highHold", "上で停止" },
                { "phase.part.fall", "下り" },
                { "phase.part.lowHold", "下で停止" },
                { "phase.corner.riseEnd", "上りの終わり" },
                { "phase.corner.fallStart", "下りの始まり" },
                { "phase.corner.fallEnd", "下りの終わり" },

                // --- Animatable value -------------------------------------
                { "animatable.spread.first", "最初の器具" },
                { "animatable.spread.last", "最後の器具" },
                { "animatable.timing", "タイミング" },
                { "animatable.timing.within", "周期内" },
                { "animatable.timing.perCycle", "周期ごと" },
                { "animatable.ownPhase", "独自の動き" },

                // --- Fade slider ------------------------------------------
                { "fade.in", "フェードイン" },
                { "fade.out", "フェードアウト" },

                // --- Effect card actions ----------------------------------
                { "card.paste", "パラメーターを貼り付け" },
                { "card.copy", "パラメーターをコピー" },

                // --- Palette strips ---------------------------------------
                { "palette.mixed.tip", "選択中のクリップでパレットが異なります。表示は最初のクリップのものです。" },
                { "palette.add", "要素を追加" },
                { "palette.delete", "選択中の要素を削除" },
                { "palette.empty", "要素がありません" },
                { "palette.solid", "単色" },
                { "palette.gradient", "グラデーション" },
                { "palette.color.addDup", "選択中の色を複製して追加" },
                { "palette.color.addWhite", "白の単色を追加" },
                { "palette.color.kind.tip", "このパレット項目の種類" },
                { "palette.gobo.close", "ゴボの一覧を閉じる" },
                { "palette.gobo.add", "ゴボを追加" },
                { "palette.gobo.addNamed", "{0} を追加" },

                // --- Language setting (editor preferences) ----------------
                { "settings.language", "言語" },
                { "settings.language.auto", "自動" },
            };
        }
    }
}
